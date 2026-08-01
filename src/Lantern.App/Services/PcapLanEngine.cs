using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Networking;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Lantern.App.Services;

public sealed class PcapLanEngine : IAsyncDisposable
{
    public static TimeSpan ProbeInterval { get; } = TimeSpan.FromMilliseconds(10);

    public static TimeSpan ProbeReplyWindow { get; } = TimeSpan.FromMilliseconds(800);

    private readonly object arpSendSync = new();
    private readonly object forwardingSendSync = new();
    private readonly SemaphoreSlim refreshSync = new(1, 1);
    private readonly SemaphoreSlim stopSync = new(1, 1);
    private readonly TrafficPolicy policy;
    private readonly DeviceRegistry registry;
    private readonly PassiveDiscoveryProfile discoveryProfile =
        PassiveDiscoveryProfile.Default;
    private readonly ClientMappingCache clientMappings = new();
    private readonly ConcurrentDictionary<string, byte> resolvingNames =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? engineCancellation;
    private Task? backgroundTask;
    private LibPcapLiveDevice? arpDevice;
    private LibPcapLiveDevice? forwardingDevice;
    private AdapterProfile? profile;
    private PhysicalAddress? gatewayMac;
    private FrameRouter? frameRouter;
    private TaskCompletionSource<PhysicalAddress>? gatewayResolution;
    private ConcurrentDictionary<IPAddress, PhysicalAddress>? activeProbeReplies;
    private ConcurrentDictionary<string, IPAddress>? activeKnownDeviceReplies;
    private KnownDeviceHint[] knownDeviceHints = [];
    private volatile bool controlling;
    private volatile bool restoring;
    private long forwardedPacketCount;
    private long droppedPacketCount;

    public PcapLanEngine(DeviceRegistry registry, TrafficPolicy policy)
    {
        this.registry = registry;
        this.policy = policy;
    }

    public bool IsRunning => arpDevice is not null || forwardingDevice is not null;

    public bool IsControlling => controlling;

    public long ForwardedPacketCount => Interlocked.Read(ref forwardedPacketCount);

    public long DroppedPacketCount => Interlocked.Read(ref droppedPacketCount);

    public string DriverName { get; private set; } = "Not started";

    public event EventHandler<string>? StatusChanged;

    public void ReplaceKnownDeviceHints(IEnumerable<KnownDeviceHint> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        knownDeviceHints = hints
            .Where(hint => !hint.MacAddress.Equals(PhysicalAddress.None))
            .DistinctBy(hint => TrafficPolicy.NormalizeMac(hint.MacAddress.ToString()))
            .ToArray();
    }

    public async Task StartAsync(AdapterProfile adapter, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        await NpfDriverService.EnsureAvailableAsync(cancellationToken);
        profile = adapter;
        clientMappings.BeginAdapter(adapter.Id);
        var clients = clientMappings.Mappings;
        restoring = false;
        Interlocked.Exchange(ref forwardedPacketCount, 0);
        Interlocked.Exchange(ref droppedPacketCount, 0);
        gatewayResolution = new TaskCompletionSource<PhysicalAddress>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var captureDevice = LibPcapLiveDeviceList.Instance
            .FirstOrDefault(candidate => candidate.MacAddress?.Equals(adapter.LocalMac) == true)
            ?? throw new InvalidOperationException(
                $"WinPcap could not match the Windows adapter “{adapter.Name}”.");

        try
        {
            arpDevice = captureDevice;
            arpDevice.OnPacketArrival += OnArpPacketArrival;
            arpDevice.Open(PcapCaptureConfiguration.CreateForArpDiscovery());
            arpDevice.Filter = "arp";
            arpDevice.StartCapture();

            forwardingDevice = new LibPcapLiveDevice(
                captureDevice.Interface ??
                throw new InvalidOperationException("The capture adapter has no interface."));
            forwardingDevice.Open(PcapCaptureConfiguration.CreateForForwarding());
            forwardingDevice.Filter = "ip";
            DriverName = captureDevice.Description ?? captureDevice.Name;
            RaiseStatus($"Resolving gateway {adapter.GatewayAddress}…");

            SendArpPacket(
                EthernetFrameCodec.BuildArpRequest(
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
                policy);

            RaiseStatus("Loading devices already observed by Windows…");
            engineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            controlling = true;
            var forwarderStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var forwardingTask = Task.Run(
                () => RunForwardingLoop(engineCancellation.Token, forwarderStarted));
            backgroundTask = forwardingTask;
            await forwarderStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);

            await RefreshNeighborsAsync(cancellationToken);

            PoisonClients();
            backgroundTask = Task.WhenAll(
                forwardingTask,
                RunMaintenanceAsync(engineCancellation.Token));
            RaiseStatus(
                "Live monitoring active — 0 KB/s is unlimited and remains visible.");
        }
        catch
        {
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
            return await RefreshNeighborsCoreAsync(cancellationToken);
        }
        finally
        {
            refreshSync.Release();
        }
    }

    private async Task<DiscoveryRefreshResult> RefreshNeighborsCoreAsync(
        CancellationToken cancellationToken)
    {
        var clients = clientMappings.Mappings;
        var activeProfile = profile ??
            throw new InvalidOperationException("Select and start an adapter before refreshing.");
        var probeReplies = new ConcurrentDictionary<IPAddress, PhysicalAddress>();
        var knownReplies = new ConcurrentDictionary<string, IPAddress>(
            StringComparer.OrdinalIgnoreCase);
        activeProbeReplies = probeReplies;
        activeKnownDeviceReplies = knownReplies;
        int probesSent;
        try
        {
            probesSent = await ProbeSubnetAsync(activeProfile, cancellationToken);
        }
        finally
        {
            activeProbeReplies = null;
            activeKnownDeviceReplies = null;
        }

        foreach (var discovered in probeReplies)
        {
            registry.Observe(
                discovered.Key,
                discovered.Value,
                DateTimeOffset.UtcNow);
        }

        await RefreshWindowsNeighborCacheAsync(
            activeProfile,
            clients,
            cancellationToken);

        return new DiscoveryRefreshResult(
            probesSent,
            probeReplies.Count,
            clients.Count);
    }

    private async Task RefreshWindowsNeighborCacheAsync(
        AdapterProfile activeProfile,
        ConcurrentDictionary<IPAddress, PhysicalAddress> clients,
        CancellationToken cancellationToken)
    {
        var neighbors = await WindowsNeighborCache.ReadAsync(
            activeProfile,
            cancellationToken);
        var requestedRealMappings = false;
        foreach (var neighbor in neighbors)
        {
            if (neighbor.Address.Equals(activeProfile.GatewayAddress))
            {
                registry.Observe(
                    neighbor.Address,
                    neighbor.MacAddress,
                    DateTimeOffset.UtcNow,
                    "Gateway");
                continue;
            }

            if (neighbor.MacAddress.Equals(activeProfile.LocalMac))
            {
                _ = await WindowsNeighborCache.DeleteAsync(
                    activeProfile,
                    neighbor.Address,
                    cancellationToken);
                SendArpPacket(
                    EthernetFrameCodec.BuildArpRequest(
                        activeProfile.LocalMac,
                        activeProfile.LocalAddress,
                        neighbor.Address));
                requestedRealMappings = true;
                continue;
            }

            var needsPoison =
                !clients.TryGetValue(neighbor.Address, out var previousMac) ||
                !previousMac.Equals(neighbor.MacAddress);
            clients[neighbor.Address] = neighbor.MacAddress;
            frameRouter?.UpdateClient(neighbor.Address, neighbor.MacAddress);
            registry.Observe(
                neighbor.Address,
                neighbor.MacAddress,
                DateTimeOffset.UtcNow);
            _ = ResolveNameAsync(neighbor.Address, neighbor.MacAddress);
            if (controlling && needsPoison)
            {
                PoisonClient(
                    new KeyValuePair<IPAddress, PhysicalAddress>(
                        neighbor.Address,
                        neighbor.MacAddress));
            }
        }

        if (requestedRealMappings)
        {
            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task<int> ProbeSubnetAsync(
        AdapterProfile activeProfile,
        CancellationToken cancellationToken)
    {
        var probesSent = 0;
        var hints = knownDeviceHints
            .Where(hint => !hint.MacAddress.Equals(activeProfile.LocalMac))
            .ToArray();

        // Try remembered addresses first. This usually rediscovers a client in
        // milliseconds without scanning the rest of the subnet.
        foreach (var hint in hints.Where(hint => hint.LastKnownIp is not null))
        {
            SendArpPacket(
                EthernetFrameCodec.BuildUnicastArpRequest(
                    activeProfile.LocalMac,
                    activeProfile.LocalAddress,
                    hint.MacAddress,
                    hint.LastKnownIp!));
            probesSent++;
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
                // Keep ordinary broadcast discovery for wired clients.
                SendArpPacket(
                    EthernetFrameCodec.BuildArpRequest(
                        activeProfile.LocalMac,
                        activeProfile.LocalAddress,
                        address));
                probesSent++;

                // Some inexpensive access points suppress broadcast ARP from
                // Ethernet to Wi-Fi. Addressing the request directly to a
                // remembered client MAC crosses those bridges without changing
                // the router or client ARP tables.
                foreach (var hint in hints)
                {
                    var key = TrafficPolicy.NormalizeMac(hint.MacAddress.ToString());
                    if (activeKnownDeviceReplies?.ContainsKey(key) == true ||
                        Equals(hint.LastKnownIp, address))
                    {
                        continue;
                    }

                    SendArpPacket(
                        EthernetFrameCodec.BuildUnicastArpRequest(
                            activeProfile.LocalMac,
                            activeProfile.LocalAddress,
                            hint.MacAddress,
                            address));
                    probesSent++;
                }
            }

            // Pace broadcasts so discovery remains invisible to games and calls.
            await Task.Delay(ProbeInterval, cancellationToken);
        }

        // Give replies time to reach the ARP capture callback before the snapshot.
        await Task.Delay(ProbeReplyWindow, cancellationToken);
        return probesSent;
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
            try
            {
                await ForwardingShutdown.RunAsync(
                    () => arpDevice is null ? Task.CompletedTask : RestoreArpAsync(),
                    () =>
                    {
                        controlling = false;
                        engineCancellation?.Cancel();
                    },
                    AwaitBackgroundTaskAsync);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RaiseStatus(
                    $"Network restoration reported an error; closing safely: {exception.Message}");
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

            if (forwardingDevice is not null)
            {
                forwardingDevice.Close();
                forwardingDevice = null;
            }

            engineCancellation?.Dispose();
            engineCancellation = null;
            backgroundTask = null;
            frameRouter = null;
            gatewayMac = null;
            profile = null;
            restoring = false;
            RaiseStatus("Stopped. Corrective ARP mappings were sent.");
        }
        finally
        {
            stopSync.Release();
        }
    }

    public async Task ApplyRuleAsync(string macAddress, TrafficRule rule)
    {
        var clients = clientMappings.Mappings;
        var previousTargets = policy.GetInterceptionTargets(macAddress);
        policy.SetRule(macAddress, rule.Normalize());
        var currentTargets = policy.GetInterceptionTargets(macAddress);
        if (controlling)
        {
            var normalizedMac = TrafficPolicy.NormalizeMac(macAddress);
            foreach (var client in clients)
            {
                if (string.Equals(
                        TrafficPolicy.NormalizeMac(client.Value.ToString()),
                        normalizedMac,
                        StringComparison.Ordinal))
                {
                    await ApplyInterceptionTransitionAsync(
                        client,
                        InterceptionTransition.Between(previousTargets, currentTargets));
                    break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private void OnArpPacketArrival(object sender, PacketCapture capture)
    {
        if (EthernetFrameCodec.TryParseArp(capture.Data, out var arp))
        {
            ObserveArp(arp);
        }
    }

    private void RunForwardingLoop(
        CancellationToken cancellationToken,
        TaskCompletionSource forwarderStarted)
    {
        try
        {
            var activeDevice = forwardingDevice ??
                throw new InvalidOperationException("The forwarding adapter is not open.");
            forwarderStarted.TrySetResult();
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
                        $"Packet forwarding stopped: {activeDevice.LastError}");
                }

                if (status != GetPacketStatus.PacketRead)
                {
                    continue;
                }

                ProcessForwardingFrame(capture.Data);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            forwarderStarted.TrySetException(exception);
            RaiseStatus(
                $"Forwarding failed; restoring normal ARP mappings: {exception.Message}");
            engineCancellation?.Cancel();
            _ = Task.Run(StopAsync);
        }
    }

    private void ProcessForwardingFrame(ReadOnlySpan<byte> bytes)
    {
        if (!controlling || frameRouter is null)
        {
            return;
        }

        var result = frameRouter.Route(bytes);
        if (result.Direction is not null && result.ClientMac is not null)
        {
            registry.RecordTraffic(
                result.ClientMac,
                result.Direction.Value,
                result.MeteredByteCount);
        }

        if (result.Action == FrameAction.Forward && result.Frame is not null)
        {
            SendForwardingPacket(result.Frame);
            Interlocked.Increment(ref forwardedPacketCount);
        }
        else if (result.Action == FrameAction.Drop)
        {
            Interlocked.Increment(ref droppedPacketCount);
        }
    }

    private void ObserveArp(ArpFrameInfo arp)
    {
        var clients = clientMappings.Mappings;
        var activeProfile = profile;
        if (activeProfile is null ||
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

        var needsPoison =
            !clients.TryGetValue(arp.SenderIp, out var previousMac) ||
            !previousMac.Equals(arp.SenderMac);
        clients[arp.SenderIp] = arp.SenderMac;
        activeProbeReplies?.TryAdd(arp.SenderIp, arp.SenderMac);
        activeKnownDeviceReplies?.TryAdd(
            TrafficPolicy.NormalizeMac(arp.SenderMac.ToString()),
            arp.SenderIp);
        frameRouter?.UpdateClient(arp.SenderIp, arp.SenderMac);
        registry.Observe(arp.SenderIp, arp.SenderMac, DateTimeOffset.UtcNow);
        _ = ResolveNameAsync(arp.SenderIp, arp.SenderMac);
        RespondToArpRequest(arp);
        if (controlling && needsPoison)
        {
            PoisonClient(
                new KeyValuePair<IPAddress, PhysicalAddress>(
                    arp.SenderIp,
                    arp.SenderMac));
        }
    }

    private async Task ResolveNameAsync(IPAddress address, PhysicalAddress mac)
    {
        var key = mac.ToString();
        if (!resolvingNames.TryAdd(key, 0))
        {
            return;
        }

        try
        {
            var entry = await Dns.GetHostEntryAsync(address)
                .WaitAsync(TimeSpan.FromSeconds(2));
            var name = entry.HostName.Split('.')[0];
            registry.SetHostName(mac, name);
        }
        catch (Exception exception) when (
            exception is System.Net.Sockets.SocketException or TimeoutException)
        {
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            RunPoisonLoopAsync(cancellationToken),
            RunNeighborRefreshLoopAsync(cancellationToken));
    }

    private async Task RunPoisonLoopAsync(CancellationToken cancellationToken)
    {
        var poisonTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await poisonTimer.WaitForNextTickAsync(cancellationToken))
            {
                PoisonClients();
            }
        }
        finally
        {
            poisonTimer.Dispose();
        }
    }

    private async Task RunNeighborRefreshLoopAsync(CancellationToken cancellationToken)
    {
        var refreshTimer = new PeriodicTimer(discoveryProfile.RefreshInterval);
        try
        {
            while (await refreshTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (discoveryProfile.ProbeSubnetOnRefresh)
                {
                    await RefreshNeighborsAsync(cancellationToken);
                }
                else
                {
                    await RefreshCachedNeighborsAsync(cancellationToken);
                }
            }
        }
        finally
        {
            refreshTimer.Dispose();
        }
    }

    private async Task RefreshCachedNeighborsAsync(CancellationToken cancellationToken)
    {
        await refreshSync.WaitAsync(cancellationToken);
        try
        {
            var activeProfile = profile;
            if (activeProfile is null)
            {
                return;
            }

            await RefreshWindowsNeighborCacheAsync(
                activeProfile,
                clientMappings.Mappings,
                cancellationToken);
        }
        finally
        {
            refreshSync.Release();
        }
    }

    private void PoisonClients()
    {
        var clients = clientMappings.Mappings;
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (!controlling || restoring || activeProfile is null || activeGatewayMac is null)
        {
            return;
        }

        foreach (var pair in clients)
        {
            PoisonClient(pair);
        }
    }

    private async Task RestoreArpAsync()
    {
        var clients = clientMappings.Mappings;
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (activeProfile is null || activeGatewayMac is null)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            foreach (var pair in clients)
            {
                SendRestoreFrames(pair, activeProfile, activeGatewayMac);
            }

            await Task.Delay(120);
        }
    }

    private void PoisonClient(KeyValuePair<IPAddress, PhysicalAddress> client)
    {
        PoisonClient(client, policy.GetInterceptionTargets(client.Value.ToString()));
    }

    private void PoisonClient(
        KeyValuePair<IPAddress, PhysicalAddress> client,
        InterceptionTargets targets)
    {
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (!controlling || restoring || targets == InterceptionTargets.None ||
            activeProfile is null || activeGatewayMac is null)
        {
            return;
        }

        var frames = ArpInterceptionFrames.BuildPoison(
            activeProfile.LocalMac,
            activeProfile.GatewayAddress,
            activeGatewayMac,
            client.Key,
            client.Value);
        foreach (var frame in frames.Select(targets))
        {
            SendArpPacket(frame);
        }

        var controllerFrames = ArpInterceptionFrames.BuildControllerProtection(
            activeProfile.LocalMac,
            activeProfile.LocalAddress,
            activeGatewayMac,
            activeProfile.GatewayAddress,
            client.Value,
            client.Key);
        SendArpPacket(controllerFrames.ClientToController);
        SendArpPacket(controllerFrames.GatewayToController);
    }

    private async Task ApplyInterceptionTransitionAsync(
        KeyValuePair<IPAddress, PhysicalAddress> client,
        InterceptionTransition transition)
    {
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (!controlling || restoring || activeProfile is null || activeGatewayMac is null)
        {
            return;
        }

        if (transition.Restore == InterceptionTargets.None)
        {
            PoisonClient(client);
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!controlling || restoring || arpDevice is null)
            {
                return;
            }

            SendRestoreFrames(client, activeProfile, activeGatewayMac);
            PoisonClient(client);
            if (attempt < 2)
            {
                await Task.Delay(40);
            }
        }
    }

    private void SendRestoreFrames(
        KeyValuePair<IPAddress, PhysicalAddress> client,
        AdapterProfile activeProfile,
        PhysicalAddress activeGatewayMac)
    {
        var recoveryRequest = ArpInterceptionFrames.BuildRecoveryRequest(
            activeProfile.LocalMac,
            activeGatewayMac,
            activeProfile.GatewayAddress,
            client.Value,
            client.Key);
        SendArpPacket(recoveryRequest);
        var controllerFrames = ArpInterceptionFrames.BuildControllerProtection(
            activeProfile.LocalMac,
            activeProfile.LocalAddress,
            activeGatewayMac,
            activeProfile.GatewayAddress,
            client.Value,
            client.Key);
        SendArpPacket(controllerFrames.ClientToController);
        SendArpPacket(controllerFrames.GatewayToController);
    }

    private void RespondToArpRequest(ArpFrameInfo request)
    {
        var clients = clientMappings.Mappings;
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (!controlling || restoring ||
            request.Operation != ArpOperation.Request ||
            activeProfile is null ||
            activeGatewayMac is null)
        {
            return;
        }

        if (request.TargetIp.Equals(activeProfile.GatewayAddress) &&
            clients.TryGetValue(request.SenderIp, out var requestingClient))
        {
            if (!policy.GetInterceptionTargets(requestingClient.ToString())
                    .HasFlag(InterceptionTargets.Client))
            {
                return;
            }

            var frames = ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGatewayMac,
                request.SenderIp,
                requestingClient);
            SendArpPacket(frames.ToClient);
            return;
        }

        if (request.SenderIp.Equals(activeProfile.GatewayAddress) &&
            clients.TryGetValue(request.TargetIp, out var requestedClient))
        {
            if (!policy.GetInterceptionTargets(requestedClient.ToString())
                    .HasFlag(InterceptionTargets.Gateway))
            {
                return;
            }

            var frames = ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGatewayMac,
                request.TargetIp,
                requestedClient);
            SendArpPacket(frames.ToGateway);
        }
    }

    private void SendArpPacket(byte[] bytes)
    {
        var activeDevice = arpDevice ??
            throw new InvalidOperationException("The ARP capture adapter is not open.");
        lock (arpSendSync)
        {
            activeDevice.SendPacket(bytes);
        }
    }

    private void SendForwardingPacket(byte[] bytes)
    {
        var activeDevice = forwardingDevice ??
            throw new InvalidOperationException("The forwarding adapter is not open.");
        lock (forwardingSendSync)
        {
            activeDevice.SendPacket(bytes);
        }
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
            RaiseStatus(
                $"Forwarding stopped unexpectedly; restoring network: {exception.Message}");
        }
    }

    private static bool IsZeroOrBroadcast(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.All(value => value == 0) || bytes.All(value => value == 0xff);
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);
}
