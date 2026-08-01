using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Lantern.App.Services;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Settings;

var expectedGateway = args.Length > 0
    ? IPAddress.Parse(args[0])
    : IPAddress.Parse("192.168.31.1");
var reportPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(AppContext.BaseDirectory, "live-probe-result.json");
if (args.Contains("--discovery-only", StringComparer.OrdinalIgnoreCase))
{
    var targetOption = args.FirstOrDefault(
        value => value.StartsWith("--target=", StringComparison.OrdinalIgnoreCase));
    var targetParts = targetOption?["--target=".Length..].Split(',');
    var targetIp = targetParts is { Length: 2 } ? IPAddress.Parse(targetParts[0]) : null;
    var targetMac = targetParts is { Length: 2 }
        ? PhysicalAddress.Parse(targetParts[1])
        : null;
    return await DiscoveryOnlyProbe.RunAsync(
        expectedGateway,
        reportPath,
        targetIp,
        targetMac);
}

var adapter = WindowsAdapterService.GetUsableAdapters()
    .FirstOrDefault(candidate => candidate.GatewayAddress.Equals(expectedGateway))
    ?? throw new InvalidOperationException(
        $"No active adapter using gateway {expectedGateway} was found.");
var registry = new DeviceRegistry();
var policy = new TrafficPolicy();
await using var engine = new PcapLanEngine(registry, policy);
engine.StatusChanged += (_, message) => Console.WriteLine($"STATUS {message}");

var savedSettings = await new SettingsStore().LoadAsync();
engine.ReplaceKnownDeviceHints(
    savedSettings.Devices.Select(pair =>
        new KnownDeviceHint(
            PhysicalAddress.Parse(pair.Key),
            IPAddress.TryParse(pair.Value.LastKnownIp, out var address)
                ? address
                : null)));

if (args.Contains("--engine-discovery", StringComparer.OrdinalIgnoreCase))
{
    var expectedMacOption = args.FirstOrDefault(
        value => value.StartsWith("--expect-mac=", StringComparison.OrdinalIgnoreCase));
    var expectedMac = expectedMacOption?["--expect-mac=".Length..]
        .Replace(":", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .ToUpperInvariant();
    IReadOnlyList<DeviceSnapshot> devices;
    try
    {
        await engine.StartAsync(adapter, CancellationToken.None);
        await Task.Delay(300);
        devices = registry.Peek();
    }
    finally
    {
        await engine.StopAsync();
    }

    var discoveredExpected = expectedMac is null || devices.Any(
        device => string.Equals(
            device.MacAddress.ToString(),
            expectedMac,
            StringComparison.OrdinalIgnoreCase));
    var discoveryReport = new
    {
        passed = discoveredExpected,
        adapter = adapter.Name,
        gateway = adapter.GatewayAddress.ToString(),
        expectedMac,
        devices = devices.Select(device => new
        {
            ip = device.IpAddress.ToString(),
            mac = device.MacAddress.ToString(),
            device.HostName,
        }),
    };
    var discoveryJson = JsonSerializer.Serialize(
        discoveryReport,
        new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(discoveryJson);
    await File.WriteAllTextAsync(reportPath, discoveryJson);
    return discoveredExpected ? 0 : 1;
}

var activePings = new List<PingResult>();
var postRestorePings = new List<PingResult>();
var trafficByMac = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
string? exercisedMac = null;
long dropsDuringPause = 0;

try
{
    await engine.StartAsync(adapter, CancellationToken.None);
    for (var sample = 0; sample < 12; sample++)
    {
        activePings.Add(await SendPingAsync());
        foreach (var device in registry.TakeSnapshot(DateTimeOffset.UtcNow))
        {
            if (device.IpAddress.Equals(adapter.GatewayAddress))
            {
                continue;
            }

            var key = device.MacAddress.ToString();
            if (key.Equals(adapter.LocalMac.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            trafficByMac[key] = trafficByMac.GetValueOrDefault(key) +
                                device.TotalBytesPerSecond;
        }

        await Task.Delay(800);
    }

    exercisedMac = trafficByMac
        .Where(pair => pair.Value > 0)
        .OrderByDescending(pair => pair.Value)
        .Select(pair => pair.Key)
        .FirstOrDefault();
    if (exercisedMac is not null)
    {
        var dropsBeforePause = engine.DroppedPacketCount;
        await engine.ApplyRuleAsync(exercisedMac, new TrafficRule(true, 0, 0));
        for (var sample = 0; sample < 4; sample++)
        {
            activePings.Add(await SendPingAsync());
            await Task.Delay(500);
        }

        dropsDuringPause = engine.DroppedPacketCount - dropsBeforePause;
        await engine.ApplyRuleAsync(exercisedMac, new TrafficRule(false, 0, 0));
    }
}
finally
{
    if (exercisedMac is not null)
    {
        await engine.ApplyRuleAsync(exercisedMac, new TrafficRule(false, 0, 0));
    }

    await engine.StopAsync();
}

for (var sample = 0; sample < 5; sample++)
{
    postRestorePings.Add(await SendPingAsync());
    await Task.Delay(200);
}

var postRestoreSelfMappings = (await WindowsNeighborCache.ReadAsync(
        adapter,
        CancellationToken.None))
    .Count(entry => entry.MacAddress.Equals(adapter.LocalMac));
var passed =
    activePings.All(result => result.Success) &&
    postRestorePings.All(result => result.Success) &&
    engine.ForwardedPacketCount > 0 &&
    exercisedMac is not null &&
    dropsDuringPause > 0 &&
    postRestoreSelfMappings == 0;
var report = new
{
    passed,
    adapter = adapter.Name,
    gateway = adapter.GatewayAddress.ToString(),
    activePingSent = activePings.Count,
    activePingSucceeded = activePings.Count(result => result.Success),
    activePingMaxMilliseconds = activePings
        .Where(result => result.Success)
        .Select(result => result.Milliseconds)
        .DefaultIfEmpty(-1)
        .Max(),
    postRestorePingSent = postRestorePings.Count,
    postRestorePingSucceeded = postRestorePings.Count(result => result.Success),
    engine.ForwardedPacketCount,
    engine.DroppedPacketCount,
    exercisedMac,
    dropsDuringPause,
    postRestoreSelfMappings,
};
var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
});
Console.WriteLine(reportJson);
await File.WriteAllTextAsync(reportPath, reportJson);
return passed ? 0 : 1;

static async Task<PingResult> SendPingAsync()
{
    using var ping = new Ping();
    try
    {
        var reply = await ping.SendPingAsync("8.8.8.8", 1_500);
        return new PingResult(
            reply.Status == IPStatus.Success,
            reply.Status == IPStatus.Success ? reply.RoundtripTime : -1,
            reply.Status.ToString());
    }
    catch (PingException exception)
    {
        return new PingResult(false, -1, exception.Message);
    }
}

internal sealed record PingResult(bool Success, long Milliseconds, string Status);
