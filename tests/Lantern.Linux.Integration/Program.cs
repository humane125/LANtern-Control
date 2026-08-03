using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Linux.Services;
using SharpPcap.LibPcap;

if (args.Length == 4 && args[0] == "--controller")
{
    return await RunControllerAsync(args[1], args[2], args[3]);
}

if (args.Length == 1 && args[0] == "--startup-failure-test")
{
    return await RunStartupFailureTestAsync();
}

Console.WriteLine($"OS: {Environment.OSVersion}");
Console.WriteLine($"User: {Environment.UserName}");

var adapters = LinuxAdapterService.GetUsableAdapters();
if (adapters.Count == 0)
{
    Console.Error.WriteLine("FAIL: LinuxAdapterService found no active IPv4 adapter with a gateway.");
    return 2;
}

foreach (var adapter in adapters)
{
    Console.WriteLine(
        $"Adapter: {adapter.Name} {adapter.LocalAddress}/{adapter.PrefixLength} " +
        $"gateway={adapter.GatewayAddress} mac={adapter.LocalMac}");
}

var pcapDevices = LibPcapLiveDeviceList.Instance;
foreach (var device in pcapDevices)
{
    Console.WriteLine($"libpcap: {device.Name} mac={device.MacAddress}");
}

var selected = adapters[0];
if (!pcapDevices.Any(device => device.MacAddress?.Equals(selected.LocalMac) == true))
{
    Console.Error.WriteLine(
        $"FAIL: libpcap did not expose the selected adapter MAC {selected.LocalMac}.");
    return 3;
}

// A /32 has no discovery hosts. It exercises the real gateway/capture/forwarding
// startup and shutdown paths without poisoning any neighboring client device.
var safeProfile = selected with { PrefixLength = 32 };
var registry = new DeviceRegistry();
var policy = new TrafficPolicy();
await using var engine = new LinuxLanEngine(registry, policy);
var runningStates = new List<bool>();
engine.StateChanged += (_, eventArgs) => runningStates.Add(eventArgs.IsRunning);
engine.StatusChanged += (_, message) => Console.WriteLine($"Status: {message}");

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
try
{
    await engine.StartAsync(safeProfile, timeout.Token);
    if (!engine.IsRunning || !engine.IsControlling)
    {
        Console.Error.WriteLine("FAIL: engine returned from StartAsync without becoming active.");
        return 4;
    }

    Console.WriteLine($"Driver: {engine.DriverName}");
    Console.WriteLine("PASS: real libpcap capture, gateway ARP resolution, and forwarding loop started.");
}
finally
{
    await engine.StopAsync();
}

if (engine.IsRunning || engine.IsControlling)
{
    Console.Error.WriteLine("FAIL: engine remained active after StopAsync.");
    return 5;
}

if (!runningStates.SequenceEqual([true, false]))
{
    Console.Error.WriteLine(
        $"FAIL: engine state notifications were [{string.Join(", ", runningStates)}], expected [true, false].");
    return 6;
}

Console.WriteLine("PASS: Linux engine stopped and completed its restoration path.");
return 0;

static async Task<int> RunControllerAsync(
    string adapterName,
    string clientMac,
    string controlDirectory)
{
    Directory.CreateDirectory(controlDirectory);
    var commandPath = Path.Combine(controlDirectory, "command");
    var statePath = Path.Combine(controlDirectory, "state");
    var adapters = LinuxAdapterService.GetUsableAdapters();
    var selected = adapters.FirstOrDefault(adapter =>
        adapter.Name.Equals(adapterName, StringComparison.Ordinal));
    if (selected is null)
    {
        Console.Error.WriteLine($"FAIL: adapter '{adapterName}' was not found.");
        return 10;
    }

    var registry = new DeviceRegistry();
    var policy = new TrafficPolicy();
    await using var engine = new LinuxLanEngine(registry, policy);
    engine.StatusChanged += (_, message) => Console.WriteLine($"Status: {message}");

    void WriteState(string name) => File.WriteAllText(
        statePath,
        $"{name} {engine.ForwardedPacketCount} {engine.DroppedPacketCount}\n");

    try
    {
        await engine.StartAsync(selected, CancellationToken.None);
        WriteState("active");
        Console.WriteLine("READY: active");

        while (true)
        {
            if (!File.Exists(commandPath))
            {
                await Task.Delay(25);
                continue;
            }

            var command = (await File.ReadAllTextAsync(commandPath)).Trim();
            File.Delete(commandPath);
            switch (command)
            {
                case "pause":
                    await engine.ApplyRuleAsync(clientMac, new TrafficRule(true, 0, 0));
                    WriteState("paused");
                    break;
                case "resume":
                    await engine.ApplyRuleAsync(clientMac, new TrafficRule(false, 0, 0));
                    WriteState("resumed");
                    break;
                case "snapshot":
                    WriteState("snapshot");
                    break;
                case "stop":
                    await engine.StopAsync();
                    WriteState("stopped");
                    return 0;
                default:
                    var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && parts[0] == "limit" &&
                        int.TryParse(parts[1], out var downloadLimit) &&
                        int.TryParse(parts[2], out var uploadLimit))
                    {
                        await engine.ApplyRuleAsync(
                            clientMac,
                            new TrafficRule(false, downloadLimit, uploadLimit));
                        WriteState("limited");
                    }
                    else
                    {
                        Console.Error.WriteLine($"Ignoring unknown command '{command}'.");
                    }
                    break;
            }
        }
    }
    finally
    {
        await engine.StopAsync();
    }
}

static async Task<int> RunStartupFailureTestAsync()
{
    var selected = LinuxAdapterService.GetUsableAdapters().FirstOrDefault();
    if (selected is null)
    {
        Console.Error.WriteLine("FAIL: no adapter available for startup failure test.");
        return 20;
    }

    var unreachableGateway = selected with
    {
        GatewayAddress = System.Net.IPAddress.Parse("198.51.100.254"),
        PrefixLength = 32,
    };
    await using var engine = new LinuxLanEngine(new DeviceRegistry(), new TrafficPolicy());
    var states = new List<LinuxEngineStateChangedEventArgs>();
    engine.StateChanged += (_, eventArgs) => states.Add(eventArgs);

    try
    {
        await engine.StartAsync(unreachableGateway, CancellationToken.None);
        Console.Error.WriteLine("FAIL: unreachable gateway unexpectedly resolved.");
        return 21;
    }
    catch (TimeoutException)
    {
    }

    var final = states.LastOrDefault();
    if (final is null || final.IsRunning ||
        !final.StatusMessage.Contains("Startup failed:", StringComparison.Ordinal) ||
        !final.StatusMessage.Contains("Stopped after an engine error:", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"FAIL: startup error was not preserved. Final status: '{final?.StatusMessage ?? "<none>"}'.");
        return 22;
    }

    Console.WriteLine($"PASS: {final.StatusMessage}");
    return 0;
}
