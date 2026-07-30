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
    private readonly object sendSync = new();
    private readonly TrafficPolicy policy;
    private readonly DeviceRegistry registry;
    private readonly ConcurrentDictionary<IPAddress, PhysicalAddress> clients = new();
    private readonly ConcurrentDictionary<string, byte> resolvingNames =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? engineCancellation;
    private Task? backgroundTask;
    private ILiveDevice? device;
    private AdapterProfile? profile;
    private PhysicalAddress? gatewayMac;
    private FrameRouter? frameRouter;
    private TaskCompletionSource<PhysicalAddress>? gatewayResolution;
    private volatile bool controlling;

    public PcapLanEngine(DeviceRegistry registry, TrafficPolicy policy)
    {
        this.registry = registry;
        this.policy = policy;
    }

    public bool IsRunning => device is not null;

    public bool IsControlling => controlling;

    public string DriverName { get; private set; } = "Not started";

    public event EventHandler<string>? StatusChanged;

    public async Task StartAsync(AdapterProfile adapter, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        await NpfDriverService.EnsureAvailableAsync(cancellationToken);
        profile = adapter;
        clients.Clear();
        gatewayResolution = new TaskCompletionSource<PhysicalAddress>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var captureDevice = LibPcapLiveDeviceList.Instance
            .FirstOrDefault(candidate => candidate.MacAddress?.Equals(adapter.LocalMac) == true)
            ?? throw new InvalidOperationException(
                $"WinPcap could not match the Windows adapter “{adapter.Name}”.");

        try
        {
            device = captureDevice;
            device.OnPacketArrival += OnPacketArrival;
            device.Open(PcapCaptureConfiguration.CreateForForwarding());
            device.Filter = "arp or ip";
            device.StartCapture();
            DriverName = captureDevice.Description ?? captureDevice.Name;
            RaiseStatus($"Resolving gateway {adapter.GatewayAddress}…");

            SendPacket(
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

            RaiseStatus($"Scanning {adapter.LocalAddress}/{adapter.PrefixLength}…");
            await ScanAsync(cancellationToken);
            await Task.Delay(800, cancellationToken);

            controlling = true;
            engineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            backgroundTask = RunMaintenanceAsync(engineCancellation.Token);
            RaiseStatus("Control active — traffic is passing through this computer.");
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var activeProfile = profile ??
            throw new InvalidOperationException("Select and start an adapter before scanning.");
        foreach (var address in SubnetScanner.EnumerateHosts(
                     activeProfile.LocalAddress,
                     activeProfile.PrefixLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (address.Equals(activeProfile.LocalAddress))
            {
                continue;
            }

            SendPacket(
                EthernetFrameCodec.BuildArpRequest(
                    activeProfile.LocalMac,
                    activeProfile.LocalAddress,
                    address));
            await Task.Delay(2, cancellationToken);
        }
    }

    public async Task StopAsync()
    {
        controlling = false;
        engineCancellation?.Cancel();
        if (backgroundTask is not null)
        {
            try
            {
                await backgroundTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (device is not null)
        {
            try
            {
                await RestoreArpAsync();
            }
            catch
            {
                // Closing the capture handle is still required if restoration fails.
            }

            try
            {
                device.StopCapture();
            }
            catch (InvalidOperationException)
            {
            }

            device.OnPacketArrival -= OnPacketArrival;
            device.Close();
            device = null;
        }

        engineCancellation?.Dispose();
        engineCancellation = null;
        backgroundTask = null;
        frameRouter = null;
        gatewayMac = null;
        profile = null;
        RaiseStatus("Stopped. Corrective ARP mappings were sent.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private void OnPacketArrival(object sender, PacketCapture capture)
    {
        var bytes = capture.GetPacket().Data;
        if (EthernetFrameCodec.TryParseArp(bytes, out var arp))
        {
            ObserveArp(arp);
            return;
        }

        if (!controlling || frameRouter is null)
        {
            return;
        }

        var result = frameRouter.Route(bytes);
        if (result.Direction is not null && result.ClientMac is not null)
        {
            registry.RecordTraffic(result.ClientMac, result.Direction.Value, bytes.Length);
        }

        if (result.Action == FrameAction.Forward && result.Frame is not null)
        {
            SendPacket(result.Frame);
        }
    }

    private void ObserveArp(ArpFrameInfo arp)
    {
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

        clients[arp.SenderIp] = arp.SenderMac;
        frameRouter?.UpdateClient(arp.SenderIp, arp.SenderMac);
        registry.Observe(arp.SenderIp, arp.SenderMac, DateTimeOffset.UtcNow);
        _ = ResolveNameAsync(arp.SenderIp, arp.SenderMac);
        RespondToArpRequest(arp);
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
        var poisonTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var scanCounter = 0;
        try
        {
            while (await poisonTimer.WaitForNextTickAsync(cancellationToken))
            {
                PoisonClients();
                scanCounter++;
                if (scanCounter >= 5)
                {
                    scanCounter = 0;
                    await ScanAsync(cancellationToken);
                }
            }
        }
        finally
        {
            poisonTimer.Dispose();
        }
    }

    private void PoisonClients()
    {
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (!controlling || activeProfile is null || activeGatewayMac is null)
        {
            return;
        }

        foreach (var pair in clients)
        {
            var frames = ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGatewayMac,
                pair.Key,
                pair.Value);
            SendPacket(frames.ToClient);
            SendPacket(frames.ToGateway);
        }
    }

    private async Task RestoreArpAsync()
    {
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
                var frames = ArpInterceptionFrames.BuildRestore(
                    activeGatewayMac,
                    activeProfile.GatewayAddress,
                    pair.Value,
                    pair.Key);
                SendPacket(frames.ToClient);
                SendPacket(frames.ToGateway);
            }

            await Task.Delay(120);
        }
    }

    private void RespondToArpRequest(ArpFrameInfo request)
    {
        var activeProfile = profile;
        var activeGatewayMac = gatewayMac;
        if (!controlling ||
            request.Operation != ArpOperation.Request ||
            activeProfile is null ||
            activeGatewayMac is null)
        {
            return;
        }

        if (request.TargetIp.Equals(activeProfile.GatewayAddress) &&
            clients.TryGetValue(request.SenderIp, out var requestingClient))
        {
            var frames = ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGatewayMac,
                request.SenderIp,
                requestingClient);
            SendPacket(frames.ToClient);
            return;
        }

        if (request.SenderIp.Equals(activeProfile.GatewayAddress) &&
            clients.TryGetValue(request.TargetIp, out var requestedClient))
        {
            var frames = ArpInterceptionFrames.BuildPoison(
                activeProfile.LocalMac,
                activeProfile.GatewayAddress,
                activeGatewayMac,
                request.TargetIp,
                requestedClient);
            SendPacket(frames.ToGateway);
        }
    }

    private void SendPacket(byte[] bytes)
    {
        var activeDevice = device ??
            throw new InvalidOperationException("The packet capture adapter is not open.");
        lock (sendSync)
        {
            activeDevice.SendPacket(bytes);
        }
    }

    private static bool IsZeroOrBroadcast(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.All(value => value == 0) || bytes.All(value => value == 0xff);
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);
}
