using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.Services;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Networking;
using Lantern.Core.Services;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Lantern.Linux.Services;

public sealed class LinuxLanEngine : IAsyncDisposable
{
    public static TimeSpan ProbeInterval { get; } = TimeSpan.FromMilliseconds(10);
    public static TimeSpan ProbeReplyWindow { get; } = TimeSpan.FromMilliseconds(800);

    private readonly DeviceRegistry registry;
    private readonly TrafficPolicy policy;
    private readonly LinuxFrameDeduplicator frameDeduplicator = new();
    private readonly object injectionSync = new();
    private readonly SemaphoreSlim refreshSync = new(1, 1);
    private readonly SemaphoreSlim stopSync = new(1, 1);
    private readonly ClientMappingCache clientMappings = new();
    private readonly ResolvedDeviceNameClaims resolvedNameClaims = new(Dns.GetHostName());
    private ConcurrentDictionary<IPAddress, PhysicalAddress> clients => clientMappings.Mappings;
    private readonly ConcurrentDictionary<string, byte> resolvingNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> learnedHostNames =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? engineCancellation;
    private Task? backgroundTask;
    private LibPcapLiveDevice? arpDevice;
    private LibPcapLiveDevice? forwardingDevice;
    private LibPcapLiveDevice? injectionDevice;
    private AdapterProfile? profile;
    private PhysicalAddress? gatewayMac;
    private FrameRouter? frameRouter;
    private LinuxFramePacer? framePacer;
    private LinuxOffloadSession? offloadSession;
    private LinuxIpForwardingSession? ipForwardingSession;
    private TaskCompletionSource<PhysicalAddress>? gatewayResolution;
    private ConcurrentDictionary<IPAddress, PhysicalAddress>? activeProbeReplies;
    private ConcurrentDictionary<string, IPAddress>? activeKnownDeviceReplies;
    private KnownDeviceHint[] knownDeviceHints = [];
    private string[] rejectedResolvedNames = [];
    private volatile bool controlling;
    private volatile bool restoring;
    private string? backgroundFailure;
    private long forwardedPacketCount;
    private long droppedPacketCount;
    private long suppressedDuplicatePacketCount;

    public LinuxLanEngine(
        DeviceRegistry registry,
        TrafficPolicy policy,
        ServiceInspectorTracker? serviceInspector = null)
    {
        this.registry = registry;
        this.policy = policy;
        ServiceInspector = serviceInspector ?? new ServiceInspectorTracker();
    }

    public bool IsRunning =>
        arpDevice is not null || forwardingDevice is not null || injectionDevice is not null;
    public bool IsControlling => controlling;
    public ServiceInspectorTracker ServiceInspector { get; }
    public long ForwardedPacketCount => Interlocked.Read(ref forwardedPacketCount);
    public long DroppedPacketCount => Interlocked.Read(ref droppedPacketCount);
    public long SuppressedDuplicatePacketCount =>
        Interlocked.Read(ref suppressedDuplicatePacketCount);
    public string DriverName { get; private set; } = "libpcap not started";

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<LinuxEngineStateChangedEventArgs>? StateChanged;
    public event EventHandler<DeviceIdentityLearnedEventArgs>? DeviceIdentityLearned;
    public event EventHandler<DeviceDomainObservedEventArgs>? DeviceDomainObserved;

    public void ReplaceKnownDeviceHints(IEnumerable<KnownDeviceHint> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        knownDeviceHints = hints
            .Where(hint => !hint.MacAddress.Equals(PhysicalAddress.None))
            .DistinctBy(hint => TrafficPolicy.NormalizeMac(hint.MacAddress.ToString()))
            .ToArray();
    }

    public void ReplaceRejectedResolvedNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        rejectedResolvedNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        resolvedNameClaims.Reset(rejectedResolvedNames);
    }

    public async Task StartAsync(AdapterProfile adapter, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        profile = adapter;
        clientMappings.BeginAdapter(adapter.Id);
        var clients = clientMappings.Mappings;
        resolvedNameClaims.Reset(rejectedResolvedNames);
        resolvingNames.Clear();
        learnedHostNames.Clear();
        registry.BeginSession();
        restoring = false;
        backgroundFailure = null;
        Interlocked.Exchange(ref forwardedPacketCount, 0);
        Interlocked.Exchange(ref droppedPacketCount, 0);
        Interlocked.Exchange(ref suppressedDuplicatePacketCount, 0);
        frameDeduplicator.Clear();
        gatewayResolution = new TaskCompletionSource<PhysicalAddress>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var captureDevice = LibPcapLiveDeviceList.Instance
            .FirstOrDefault(candidate => candidate.MacAddress?.Equals(adapter.LocalMac) == true)
            ?? throw new InvalidOperationException(
                $"libpcap could not match the Linux adapter '{adapter.Name}'.");

        try
        {
            RaiseStatus($"Preparing adapter {adapter.Name} for packet forwarding...");
            ipForwardingSession = await LinuxIpForwardingManager.DisableAsync(
                cancellationToken);
            offloadSession = await LinuxOffloadManager.DisableAsync(
                adapter.Name,
                cancellationToken);

            arpDevice = captureDevice;
            arpDevice.OnPacketArrival += OnArpPacketArrival;
            arpDevice.Open(LinuxCaptureConfiguration.CreateForArpDiscovery());
            // DHCP is broadcast before a client necessarily exists in our ARP
            // table. Capture it alongside ARP so a device name learned once can
            // be persisted and reused across future launches/IP changes.
            arpDevice.Filter = "arp or (udp and (port 67 or port 68))";
            arpDevice.StartCapture();

            forwardingDevice = new LibPcapLiveDevice(
                captureDevice.Interface ??
                throw new InvalidOperationException("The capture adapter has no interface."));
            forwardingDevice.Open(LinuxCaptureConfiguration.CreateForForwarding());
            forwardingDevice.Filter = LinuxCaptureFilter.ForForwarding(adapter.LocalMac);

            // Capture and injection use different native pcap handles. Calling
            // pcap_next_ex and pcap_sendpacket concurrently on one handle is not
            // portable and can stop physical Wi-Fi adapters even when a veth test
            // passes. All emitted ARP and forwarded Ethernet frames are serialized
            // through this dedicated injection handle.
            injectionDevice = new LibPcapLiveDevice(
                captureDevice.Interface ??
                throw new InvalidOperationException("The capture adapter has no interface."));
            injectionDevice.Open(LinuxCaptureConfiguration.CreateForForwarding());
            DriverName = captureDevice.Description ?? captureDevice.Name;
            RaiseStatus($"Resolving gateway {adapter.GatewayAddress}...");
            SendArpPacket(EthernetFrameCodec.BuildArpRequest(
                adapter.LocalMac,
                adapter.LocalAddress,
                adapter.GatewayAddress));

            gatewayMac = await gatewayResolution.Task.WaitAsync(
                TimeSpan.FromSeconds(4),
                cancellationToken);
            registry.Observe(
                adapter.GatewayAddress,
                gatewayMac,
                DateTimeOffset.UtcNow,
                "Gateway");
            frameRouter = new FrameRouter(
                adapter.LocalMac,
                adapter.LocalAddress,
                gatewayMac,
                clients,
                policy,
                enforceRateLimits: false);
            framePacer = new LinuxFramePacer(
                policy,
                frame =>
                {
                    SendForwardingPacket(frame);
                    Interlocked.Increment(ref forwardedPacketCount);
                },
                frameDropped: () => Interlocked.Increment(ref droppedPacketCount),
                failed: exception =>
                {
                    HandleBackgroundFailure("Traffic pacing failed", exception);
                });

            engineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            controlling = true;
            var forwarderStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var forwardingTask = Task.Run(
                () => RunForwardingLoop(engineCancellation.Token, forwarderStarted));
            await forwarderStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            await RefreshNeighborsAsync(cancellationToken);
            PoisonClients();
            backgroundTask = Task.WhenAll(
                forwardingTask,
                RunMaintenanceAsync(engineCancellation.Token));
            const string activeMessage =
                "Linux control active - live traffic is routed through this computer.";
            RaiseStatus(activeMessage);
            RaiseStateChanged(true, activeMessage);
        }
        catch (Exception exception)
        {
            // StopAsync performs the safety restoration, but it must not replace
            // the startup exception with a generic restoration message. The UI
            // receives StateChanged asynchronously, so preserving the failure here
            // also prevents that queued notification from hiding the real cause.
            Interlocked.CompareExchange(
                ref backgroundFailure,
                $"Startup failed: {exception.Message}",
                null);
            await StopAsync();
            throw;
        }
    }

    public async Task<DiscoveryRefreshResult> RefreshNeighborsAsync(
        CancellationToken cancellationToken = default)
    {
        await refreshSync.WaitAsync(cancellationToken);
        try
        {
            var activeProfile = profile ??
                throw new InvalidOperationException("Start an adapter before refreshing devices.");
            var replies = new ConcurrentDictionary<IPAddress, PhysicalAddress>();
            var knownReplies = new ConcurrentDictionary<string, IPAddress>(
                StringComparer.OrdinalIgnoreCase);
            activeProbeReplies = replies;
            activeKnownDeviceReplies = knownReplies;
            int probes;
            try
            {
                probes = await ProbeCachedNeighborsAsync(
                    activeProfile,
                    cancellationToken);
                probes += await ProbeSubnetAsync(activeProfile, cancellationToken);
                var cached = await LinuxArpCache.ReadAsync(
                    activeProfile.Name,
                    activeProfile.LocalAddress,
                    activeProfile.PrefixLength,
                    cancellationToken);
                _ = ImportCachedNeighbors(activeProfile, cached);
            }
            finally
            {
                activeProbeReplies = null;
                activeKnownDeviceReplies = null;
            }

            return new DiscoveryRefreshResult(probes, replies.Count, clients.Count);
        }
        finally
        {
            refreshSync.Release();
        }
    }

    public async Task ApplyRuleAsync(string macAddress, TrafficRule rule)
    {
        policy.SetRule(macAddress, rule.Normalize());
        if (framePacer is not null)
        {
            await framePacer.ResetAsync(PhysicalAddress.Parse(
                TrafficPolicy.NormalizeMac(macAddress)));
        }

        if (!controlling)
        {
            return;
        }

        var normalized = TrafficPolicy.NormalizeMac(macAddress);
        var client = clients.FirstOrDefault(pair =>
            TrafficPolicy.NormalizeMac(pair.Value.ToString()) == normalized);
        if (!client.Equals(default(KeyValuePair<IPAddress, PhysicalAddress>)))
        {
            PoisonClient(client);
        }
    }

    public async Task StopAsync()
    {
        await stopSync.WaitAsync();
        try
        {
            if (!IsRunning && profile is null)
            {
                return;
            }

            restoring = true;
            controlling = false;
            engineCancellation?.Cancel();
            await AwaitBackgroundTaskAsync();
            if (framePacer is not null)
            {
                await framePacer.DisposeAsync();
                framePacer = null;
            }

            try
            {
                await RestoreArpAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RaiseStatus($"ARP restoration reported an error: {exception.Message}");
            }

            if (arpDevice is not null)
            {
                try
                {
                    arpDevice.StopCapture();
                }
                catch (InvalidOperationException)
                {
                }

                arpDevice.OnPacketArrival -= OnArpPacketArrival;
                arpDevice.Close();
                arpDevice = null;
            }

            forwardingDevice?.Close();
            forwardingDevice = null;
            injectionDevice?.Close();
            injectionDevice = null;
            if (offloadSession is not null)
            {
                try
                {
                    await offloadSession.DisposeAsync();
                }
                catch (Exception exception)
                {
                    RaiseStatus($"Adapter offload restoration reported an error: {exception.Message}");
                }

                offloadSession = null;
            }
            if (ipForwardingSession is not null)
            {
                try
                {
                    await ipForwardingSession.DisposeAsync();
                }
                catch (Exception exception)
                {
                    RaiseStatus(
                        $"Kernel IPv4 forwarding restoration reported an error: {exception.Message}");
                }

                ipForwardingSession = null;
            }
            engineCancellation?.Dispose();
            engineCancellation = null;
            backgroundTask = null;
            frameRouter = null;
            gatewayMac = null;
            profile = null;
            restoring = false;
            var failure = backgroundFailure;
            backgroundFailure = null;
            var stoppedMessage = failure is null
                ? "Stopped. Corrective ARP mappings were sent."
                : $"Stopped after an engine error: {failure}. Corrective ARP mappings were sent.";
            RaiseStatus(stoppedMessage);
            RaiseStateChanged(false, stoppedMessage, failure);
        }
        finally
        {
            stopSync.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<int> ProbeSubnetAsync(
        AdapterProfile activeProfile,
        CancellationToken cancellationToken)
    {
        var probes = 0;
        var hints = knownDeviceHints
            .Where(hint => !hint.MacAddress.Equals(activeProfile.LocalMac))
            .ToArray();

        // First retry remembered IP/MAC pairs. Sleeping Wi-Fi clients often
        // answer a directed ARP even when an access point suppresses broadcast
        // ARP between wired and wireless stations.
        foreach (var hint in hints.Where(hint => hint.LastKnownIp is not null))
        {
            SendArpPacket(EthernetFrameCodec.BuildUnicastArpRequest(
                activeProfile.LocalMac,
                activeProfile.LocalAddress,
                hint.MacAddress,
                hint.LastKnownIp!));
            probes++;
        }

        if (hints.Any(hint => hint.LastKnownIp is not null))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }

        foreach (var address in IPv4DiscoveryRange.EnumerateHosts(
                     activeProfile.LocalAddress,
                     activeProfile.PrefixLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!address.Equals(activeProfile.GatewayAddress))
            {
                SendArpPacket(EthernetFrameCodec.BuildArpRequest(
                    activeProfile.LocalMac,
                    activeProfile.LocalAddress,
                    address));
                probes++;

                // Match the Windows discovery path: for a remembered MAC whose
                // address changed, also try every candidate IP as a unicast ARP.
                // This crosses consumer AP bridges that hide broadcast probes.
                foreach (var hint in hints)
                {
                    var key = TrafficPolicy.NormalizeMac(hint.MacAddress.ToString());
                    if (activeKnownDeviceReplies?.ContainsKey(key) == true ||
                        Equals(hint.LastKnownIp, address))
                    {
                        continue;
                    }

                    SendArpPacket(EthernetFrameCodec.BuildUnicastArpRequest(
                        activeProfile.LocalMac,
                        activeProfile.LocalAddress,
                        hint.MacAddress,
                        address));
                    probes++;
                }
            }

            await Task.Delay(ProbeInterval, cancellationToken);
        }

        await Task.Delay(ProbeReplyWindow, cancellationToken);
        return probes;
    }

    private async Task<int> ProbeCachedNeighborsAsync(
        AdapterProfile activeProfile,
        CancellationToken cancellationToken)
    {
        var cached = await LinuxArpCache.ReadAsync(
            activeProfile.Name,
            activeProfile.LocalAddress,
            activeProfile.PrefixLength,
            cancellationToken);
        var probes = 0;
        foreach (var entry in cached)
        {
            if (entry.Address.Equals(activeProfile.GatewayAddress))
            {
                continue;
            }

            SendArpPacket(EthernetFrameCodec.BuildUnicastArpRequest(
                activeProfile.LocalMac,
                activeProfile.LocalAddress,
                entry.MacAddress,
                entry.Address));
            probes++;
        }

        if (probes > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }

        return probes;
    }

    private int ImportCachedNeighbors(
        AdapterProfile activeProfile,
        IReadOnlyList<LinuxArpCacheEntry> cached)
    {
        var imported = 0;
        foreach (var entry in cached)
        {
            if (entry.Address.Equals(activeProfile.GatewayAddress))
            {
                registry.Observe(
                    entry.Address,
                    entry.MacAddress,
                    DateTimeOffset.UtcNow,
                    "Gateway");
                continue;
            }

            // A peer mapped to this computer's MAC is stale interception state,
            // not a real client identity. Importing it creates duplicate entries
            // named after the controller and can poison the wrong host.
            if (entry.MacAddress.Equals(activeProfile.LocalMac))
            {
                continue;
            }

            var changed = clientMappings.Upsert(entry.Address, entry.MacAddress);
            frameRouter?.UpdateClient(entry.Address, entry.MacAddress);
            registry.Remember(
                entry.Address,
                entry.MacAddress,
                DateTimeOffset.UtcNow,
                GetKnownHostName(entry.MacAddress));
            _ = ResolveNameAsync(entry.Address, entry.MacAddress);
            if (controlling && changed)
            {
                PoisonClient(new KeyValuePair<IPAddress, PhysicalAddress>(
                    entry.Address,
                    entry.MacAddress));
            }

            imported++;
        }

        return imported;
    }

    private void OnArpPacketArrival(object sender, PacketCapture capture)
    {
        if (EthernetFrameCodec.TryParseArp(capture.Data, out var arp))
        {
            ObserveArp(arp);
            return;
        }

        if (DhcpHostNameParser.TryParse(capture.Data, out var host))
        {
            ObserveHostName(host);
        }
    }

    private void ObserveArp(ArpFrameInfo arp)
    {
        var activeProfile = profile;
        if (activeProfile is null ||
            !arp.HasConsistentSender ||
            arp.SenderIp.Equals(IPAddress.Any) ||
            arp.SenderMac.Equals(activeProfile.LocalMac) ||
            IsZeroOrBroadcast(arp.SenderMac))
        {
            return;
        }

        if (arp.SenderIp.Equals(activeProfile.GatewayAddress))
        {
            gatewayResolution?.TrySetResult(arp.SenderMac);
            RespondToArpRequest(arp);
            return;
        }

        var changed = clientMappings.Upsert(arp.SenderIp, arp.SenderMac);
        activeProbeReplies?.TryAdd(arp.SenderIp, arp.SenderMac);
        activeKnownDeviceReplies?.TryAdd(
            TrafficPolicy.NormalizeMac(arp.SenderMac.ToString()),
            arp.SenderIp);
        frameRouter?.UpdateClient(arp.SenderIp, arp.SenderMac);
        registry.Observe(
            arp.SenderIp,
            arp.SenderMac,
            DateTimeOffset.UtcNow,
            GetKnownHostName(arp.SenderMac));
        _ = ResolveNameAsync(arp.SenderIp, arp.SenderMac);
        RespondToArpRequest(arp);
        if (controlling && changed)
        {
            PoisonClient(new KeyValuePair<IPAddress, PhysicalAddress>(
                arp.SenderIp,
                arp.SenderMac));
        }
    }

    private void RunForwardingLoop(
        CancellationToken cancellationToken,
        TaskCompletionSource started)
    {
        try
        {
            var activeDevice = forwardingDevice ??
                throw new InvalidOperationException("The forwarding adapter is not open.");
            started.TrySetResult();
            while (!cancellationToken.IsCancellationRequested)
            {
                var status = activeDevice.GetNextPacket(out var capture);
                if (status == GetPacketStatus.ReadTimeout)
                {
                    continue;
                }

                if (status == GetPacketStatus.Error)
                {
                    throw new InvalidOperationException(
                        $"libpcap forwarding stopped: {activeDevice.LastError}");
                }

                if (status == GetPacketStatus.PacketRead)
                {
                    ProcessForwardingFrame(capture.Data);
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            started.TrySetException(exception);
            HandleBackgroundFailure("Forwarding failed", exception);
        }
    }

    private void ProcessForwardingFrame(ReadOnlySpan<byte> bytes)
    {
        if (!controlling || frameRouter is null)
        {
            return;
        }

        // Some Linux AF_PACKET/libpcap combinations surface an injected or
        // bridged Ethernet frame twice in immediate mode. Never forward or
        // meter the second copy; doing so halves TCP goodput while the target
        // device reports roughly twice the useful download rate.
        if (frameDeduplicator.IsDuplicate(bytes))
        {
            Interlocked.Increment(ref suppressedDuplicatePacketCount);
            return;
        }

        if (DhcpHostNameParser.TryParse(bytes, out var host))
        {
            ObserveHostName(host);
        }

        var result = frameRouter.Route(bytes);
        var observedAt = DateTimeOffset.UtcNow;
        try
        {
            ServiceInspector.Observe(result, observedAt);
        }
        catch (Exception)
        {
            // Usage accounting is optional telemetry. It must never interrupt forwarding.
        }
        if (result.Direction is { } direction && result.ClientMac is { } clientMac)
        {
            registry.RecordTraffic(clientMac, direction, result.MeteredByteCount);
        }

        if (result.Direction == TrafficDirection.Upload &&
            result.ClientMac is { } domainClient &&
            result.Observation is { } observation)
        {
            DeviceDomainObserved?.Invoke(
                this,
                new DeviceDomainObservedEventArgs(
                    domainClient,
                    observation,
                    observedAt,
                    result.BlockedByDomain));
        }

        if (result.Action == FrameAction.Forward &&
            result.Frame is not null &&
            result.Direction is { } forwardingDirection &&
            result.ClientMac is { } forwardingClient)
        {
            var rule = policy.GetRule(forwardingClient.ToString());
            if (!LinuxForwardingStrategy.RequiresPacing(rule, forwardingDirection))
            {
                // Unlimited packets must not cross an extra channel and worker
                // boundary. Sending them immediately from the capture loop keeps
                // Linux forwarding latency comparable to Npcap on Windows while
                // limited traffic still uses the paced per-device queues below.
                SendForwardingPacket(result.Frame);
                Interlocked.Increment(ref forwardedPacketCount);
            }
            else if (framePacer?.TryEnqueue(
                         forwardingClient,
                         forwardingDirection,
                         result.Frame) != true)
            {
                Interlocked.Increment(ref droppedPacketCount);
            }
        }
        else if (result.Action == FrameAction.Drop)
        {
            Interlocked.Increment(ref droppedPacketCount);
        }
    }

    private async Task ResolveNameAsync(IPAddress address, PhysicalAddress mac)
    {
        var key = TrafficPolicy.NormalizeMac(mac.ToString());
        if (!resolvingNames.TryAdd(key, 0))
        {
            return;
        }

        try
        {
            var netBiosTask = NetBiosNameResolver.ResolveAsync(address);
            var mdnsTask = MdnsNameResolver.ResolveAsync(address);
            var dnsTask = ResolveDnsNameAsync(address);
            await Task.WhenAll(netBiosTask, mdnsTask, dnsTask);
            foreach (var name in new[] { netBiosTask.Result, mdnsTask.Result, dnsTask.Result })
            {
                if (resolvedNameClaims.TryClaim(
                        mac,
                        name,
                        out var acceptedName,
                        out var isNewClaim))
                {
                    registry.SetHostName(mac, acceptedName);
                    if (isNewClaim)
                    {
                        DeviceIdentityLearned?.Invoke(
                            this,
                            new DeviceIdentityLearnedEventArgs(mac, acceptedName));
                    }

                    return;
                }
            }
        }
        finally
        {
            resolvingNames.TryRemove(key, out _);
        }
    }

    private void ObserveHostName(DhcpHostNameInfo host)
    {
        var key = TrafficPolicy.NormalizeMac(host.MacAddress.ToString());
        registry.SetHostName(host.MacAddress, host.HostName);
        if (learnedHostNames.TryGetValue(key, out var previous) &&
            string.Equals(previous, host.HostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        learnedHostNames[key] = host.HostName;
        DeviceIdentityLearned?.Invoke(
            this,
            new DeviceIdentityLearnedEventArgs(host.MacAddress, host.HostName));
    }

    private static async Task<string?> ResolveDnsNameAsync(IPAddress address)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(address).WaitAsync(TimeSpan.FromSeconds(2));
            var name = entry.HostName.Split('.')[0].Trim();
            return name.Length == 0 || IPAddress.TryParse(name, out _) ? null : name;
        }
        catch (Exception exception) when (
            exception is System.Net.Sockets.SocketException or TimeoutException)
        {
            return null;
        }
    }

    private string? GetKnownHostName(PhysicalAddress mac)
    {
        var key = TrafficPolicy.NormalizeMac(mac.ToString());
        return knownDeviceHints.FirstOrDefault(hint =>
            TrafficPolicy.NormalizeMac(hint.MacAddress.ToString()) == key)?.HostName;
    }

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        using var poisonTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        using var probeTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var poisonTask = RunTimerAsync(poisonTimer, PoisonClients, cancellationToken);
        var probeTask = RunTimerAsync(
            probeTimer,
            () =>
            {
                if (profile is { } activeProfile)
                {
                    ProbeKnownClients(activeProfile);
                }
            },
            cancellationToken);
        await Task.WhenAll(poisonTask, probeTask);
    }

    private static async Task RunTimerAsync(
        PeriodicTimer timer,
        Action action,
        CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            action();
        }
    }

    private void ProbeKnownClients(AdapterProfile activeProfile)
    {
        foreach (var client in clients)
        {
            SendArpPacket(EthernetFrameCodec.BuildUnicastArpRequest(
                activeProfile.LocalMac,
                activeProfile.LocalAddress,
                client.Value,
                client.Key));
        }
    }

    private void PoisonClients()
    {
        if (!controlling || restoring)
        {
            return;
        }

        foreach (var client in clients)
        {
            PoisonClient(client);
        }
    }

    private void PoisonClient(KeyValuePair<IPAddress, PhysicalAddress> client)
    {
        var activeProfile = profile;
        var activeGateway = gatewayMac;
        if (!controlling || restoring || activeProfile is null || activeGateway is null)
        {
            return;
        }

        var frames = ArpInterceptionFrames.BuildPoison(
            activeProfile.LocalMac,
            activeProfile.GatewayAddress,
            activeGateway,
            client.Key,
            client.Value);
        foreach (var frame in frames.Select(policy.GetInterceptionTargets(client.Value.ToString())))
        {
            SendArpPacket(frame);
        }

    }

    private void RespondToArpRequest(ArpFrameInfo request)
    {
        var activeProfile = profile;
        var activeGateway = gatewayMac;
        if (!controlling || restoring || request.Operation != ArpOperation.Request ||
            activeProfile is null || activeGateway is null)
        {
            return;
        }

        if (request.TargetIp.Equals(activeProfile.GatewayAddress) &&
            clients.TryGetValue(request.SenderIp, out var clientMac))
        {
            SendArpPacket(ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGateway,
                request.SenderIp,
                clientMac).ToClient);
        }
        else if (request.SenderIp.Equals(activeProfile.GatewayAddress) &&
                 clients.TryGetValue(request.TargetIp, out var requestedMac))
        {
            SendArpPacket(ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGateway,
                request.TargetIp,
                requestedMac).ToGateway);
        }
    }

    private async Task RestoreArpAsync()
    {
        var activeProfile = profile;
        var activeGateway = gatewayMac;
        if (activeProfile is null || activeGateway is null || injectionDevice is null)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            foreach (var client in clients)
            {
                SendRestoreFrames(client, activeProfile, activeGateway);
            }

            await Task.Delay(120);
        }
    }

    private void SendRestoreFrames(
        KeyValuePair<IPAddress, PhysicalAddress> client,
        AdapterProfile activeProfile,
        PhysicalAddress activeGateway)
    {
        var restore = ArpInterceptionFrames.BuildRestore(
            activeProfile.LocalMac,
            activeProfile.GatewayAddress,
            activeGateway,
            client.Key,
            client.Value);
        SendArpPacket(restore.ToClient);
        SendArpPacket(restore.ToGateway);
        SendArpPacket(ArpInterceptionFrames.BuildRecoveryRequest(
            activeProfile.LocalMac,
            activeGateway,
            activeProfile.GatewayAddress,
            client.Value,
            client.Key));
        SendArpPacket(EthernetFrameCodec.BuildUnicastArpRequest(
            client.Value,
            client.Key,
            activeGateway,
            activeProfile.GatewayAddress));
    }

    private async Task AwaitBackgroundTaskAsync()
    {
        if (backgroundTask is null)
        {
            return;
        }

        try
        {
            await backgroundTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            RaiseStatus($"Forwarding stopped unexpectedly: {exception.Message}");
        }
    }

    private void SendArpPacket(byte[] bytes)
    {
        SendPacket(bytes);
    }

    private void SendForwardingPacket(byte[] bytes)
    {
        SendPacket(bytes);
    }

    private void SendPacket(byte[] bytes)
    {
        var active = injectionDevice ??
            throw new InvalidOperationException("The packet injection adapter is not open.");
        lock (injectionSync)
        {
            active.SendPacket(bytes);
        }
    }

    private static bool IsZeroOrBroadcast(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.All(value => value == 0) || bytes.All(value => value == 0xff);
    }

    private void HandleBackgroundFailure(string source, Exception exception)
    {
        var failure = $"{source}: {exception.Message}";
        Interlocked.CompareExchange(ref backgroundFailure, failure, null);
        RaiseStatus($"{failure}; restoring the network.");
        engineCancellation?.Cancel();
        _ = Task.Run(StopAsync);
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);

    private void RaiseStateChanged(
        bool isRunning,
        string statusMessage,
        string? failureMessage = null) =>
        StateChanged?.Invoke(
            this,
            new LinuxEngineStateChangedEventArgs(
                isRunning,
                statusMessage,
                failureMessage));
}
