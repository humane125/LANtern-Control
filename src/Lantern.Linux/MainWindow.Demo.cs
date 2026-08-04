using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.Services;
using Lantern.App.ViewModels;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Networking;
using Lantern.Core.Settings;
using Lantern.Core.Services;
using Lantern.Linux.ViewModels;

namespace Lantern.Linux;

public partial class MainWindow
{
    private int demoTick;

    private static readonly DemoDeviceSeed[] DemoDevices =
    [
        new("Aurora Laptop", "192.168.50.24", "0E4F69CCE4F0", 2_820_000, 186_000, 3500, 120, false),
        new("Living Room TV", "192.168.50.38", "F69833D200F1", 1_140_000, 18_500, 0, 0, false),
        new("Nova Phone", "192.168.50.57", "D2574CDC A5B2".Replace(" ", string.Empty), 742_000, 92_000, 1200, 240, false),
        new("Game Console", "192.168.50.72", "2281D7FB3C1E", 516_000, 123_000, 0, 0, false),
    ];

    private void LoadDemoData()
    {
        var now = DateTimeOffset.UtcNow;
        var adapter = new AdapterProfile(
            "demo-ethernet",
            "Ethernet",
            "LANtern demo adapter",
            IPAddress.Parse("192.168.50.10"),
            24,
            IPAddress.Parse("192.168.50.1"),
            PhysicalAddress.Parse("345A6063C052"));
        AdapterSelector.ItemsSource = new[] { adapter };
        AdapterSelector.SelectedIndex = 0;

        settings = new AppSettings();
        dashboardState = new LinuxDashboardState(settings, policy);
        devicesByMac.Clear();
        Devices.Clear();
        foreach (var seed in DemoDevices)
        {
            var preferences = new DevicePreferences
            {
                Alias = seed.Name,
                LearnedHostName = seed.Name,
                LastKnownIp = seed.IpAddress,
                DownloadKiloBytesPerSecond = seed.DownloadLimit,
                UploadKiloBytesPerSecond = seed.UploadLimit,
                PauseInternet = seed.Paused,
            };
            settings.Devices[seed.MacKey] = preferences;
            var device = new DeviceViewModel(OnDeviceRuleChangedAsync);
            device.Initialize(CreateDemoSnapshot(seed, now, seed.Download, seed.Upload), preferences, false, "Online");
            device.SetPresence(true);
            devicesByMac[seed.MacKey] = device;
            Devices.Add(device);
        }

        var gatewaySeed = new DemoDeviceSeed(
            "Gateway", "192.168.50.1", "64644A380A15", 0, 0, 0, 0, false);
        var gateway = new DeviceViewModel(OnDeviceRuleChangedAsync);
        gateway.Initialize(
            CreateDemoSnapshot(gatewaySeed, now, 0, 0),
            new DevicePreferences { Alias = "Gateway", LastKnownIp = gatewaySeed.IpAddress },
            true,
            "Gateway — protected");
        gateway.SetPresence(true);
        devicesByMac[gatewaySeed.MacKey] = gateway;
        Devices.Add(gateway);

        SyncDomainRuleDevices();
        var youtube = DomainBlockPresetCatalog.All.Single(preset => preset.Name == "YouTube");
        settings.BlockedDomains[DemoDevices[0].MacKey] = youtube.Domains.ToList();
        settings.AppliedDomainPresets[DemoDevices[0].MacKey] = [youtube.Name];
        settings.BlockedDomains[DemoDevices[2].MacKey] = ["instagram.com", "cdninstagram.com"];
        ApplyAllSavedRules();
        LoadDomainRulesFromSettings();

        AddDemoActivity(DemoDevices[0], "youtube.com", DomainObservationSource.Dns, 14, true);
        AddDemoActivity(DemoDevices[0], "googlevideo.com", DomainObservationSource.Tls, 9, true);
        AddDemoActivity(DemoDevices[0], "api.github.com", DomainObservationSource.Dns, 8, false, DomainObservationSource.Tls);
        AddDemoActivity(DemoDevices[0], "fonts.gstatic.com", DomainObservationSource.Tls, 5, false);
        AddDemoActivity(DemoDevices[1], "netflix.com", DomainObservationSource.Dns, 12, false, DomainObservationSource.Tls);
        AddDemoActivity(DemoDevices[1], "nflxvideo.net", DomainObservationSource.Tls, 19, false);
        AddDemoActivity(DemoDevices[2], "instagram.com", DomainObservationSource.Dns, 16, true, DomainObservationSource.Tls);
        AddDemoActivity(DemoDevices[2], "cdninstagram.com", DomainObservationSource.Tls, 11, true);
        AddDemoActivity(DemoDevices[3], "discord.com", DomainObservationSource.Dns, 7, false, DomainObservationSource.Tls);
        LoadDemoServiceInspector(now);

        trafficHistory.Clear();
        for (var index = 89; index >= 0; index--)
        {
            var phase = 89 - index;
            var deviceTraffic = CreateDemoTraffic(phase);
            var top = deviceTraffic.OrderByDescending(item => item.TotalBytesPerSecond).First();
            trafficHistory.Add(new TrafficSample(
                now.AddSeconds(-index),
                deviceTraffic.Sum(item => item.DownloadBytesPerSecond),
                deviceTraffic.Sum(item => item.UploadBytesPerSecond),
                top.DeviceName,
                top.DownloadBytesPerSecond,
                top.UploadBytesPerSecond,
                deviceTraffic));
        }

        TrafficChart.SetSamples(trafficHistory.Samples);
        startedAt = now.AddMinutes(-14).AddSeconds(-37);
        SetActiveState(true);
        SetStatus("Demo preview — simulated local traffic; no packets are captured or changed.");
        RefreshDemoDashboard(addSample: false);
        UpdateButtons();
        UpdateEmptyStates();
    }

    private void RefreshDemoDashboard(bool addSample = true)
    {
        demoTick++;
        var now = DateTimeOffset.UtcNow;
        var traffic = CreateDemoTraffic(90 + demoTick);
        for (var index = 0; index < DemoDevices.Length; index++)
        {
            var seed = DemoDevices[index];
            devicesByMac[seed.MacKey].Update(
                CreateDemoSnapshot(
                    seed,
                    now,
                    traffic[index].DownloadBytesPerSecond,
                    traffic[index].UploadBytesPerSecond),
                seed.Name);
        }

        var summary = DashboardSummary.From(Devices.Where(device => device.IsOnline));
        DeviceCountText.Text = summary.ConnectedDevices.ToString();
        DeviceCountLabel.Text = $"{summary.ConnectedDevices} devices";
        DownloadTotalText.Text = DeviceViewModel.FormatRate(summary.DownloadBytesPerSecond);
        UploadTotalText.Text = DeviceViewModel.FormatRate(summary.UploadBytesPerSecond);
        ActiveRulesText.Text = summary.ActiveRules.ToString();
        var top = summary.DeviceTraffic.FirstOrDefault();
        TopDeviceText.Text = top is null
            ? "Waiting for traffic"
            : $"{top.DeviceName}  ↓ {DeviceViewModel.FormatRate(top.DownloadBytesPerSecond)}  ↑ {DeviceViewModel.FormatRate(top.UploadBytesPerSecond)}";
        UptimeText.Text = startedAt is { } start ? $"Active {FormatDuration(now - start)}" : "Active";
        EmptyDeviceText.IsVisible = false;

        if (addSample && top is not null)
        {
            trafficHistory.TryAdd(new TrafficSample(
                now,
                summary.DownloadBytesPerSecond,
                summary.UploadBytesPerSecond,
                top.DeviceName,
                top.DownloadBytesPerSecond,
                top.UploadBytesPerSecond,
                summary.DeviceTraffic), TrafficSamplingProfile.Interval);
            TrafficChart.SetSamples(trafficHistory.Samples);
        }
    }

    private void AddDemoActivity(
        DemoDeviceSeed seed,
        string domain,
        DomainObservationSource source,
        int hits,
        bool blocked,
        DomainObservationSource? secondarySource = null)
    {
        var observedAt = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(1, 18 - hits));
        for (var index = 0; index < hits; index++)
        {
            var currentSource = secondarySource is not null && index % 2 == 1
                ? secondarySource.Value
                : source;
            ObserveDomain(new DeviceDomainObservedEventArgs(
                PhysicalAddress.Parse(seed.MacKey),
                new DomainObservation(domain, currentSource, IPAddress.Parse("203.0.113.20")),
                observedAt.AddMilliseconds(index * 40),
                blocked));
        }
    }

    private void LoadDemoServiceInspector(DateTimeOffset now)
    {
        ServiceSessionSnapshot[] snapshots =
        [
            new(DemoDevices[0].MacKey, "youtube", "YouTube", now.AddMinutes(-12),
                now.AddSeconds(-2), TimeSpan.FromMinutes(12), 1_840_000_000, 24_000_000,
                1_920_000, 18_400, 6, true),
            new(DemoDevices[0].MacKey, "discord", "Discord", now.AddMinutes(-7),
                now.AddSeconds(-4), TimeSpan.FromMinutes(7), 86_000_000, 31_000_000,
                62_000, 14_500, 4, true),
            new(DemoDevices[1].MacKey, "netflix", "Netflix", now.AddMinutes(-18),
                now.AddSeconds(-1), TimeSpan.FromMinutes(18), 3_240_000_000, 8_400_000,
                1_080_000, 2_800, 5, true),
            new(DemoDevices[2].MacKey, "instagram", "Instagram", now.AddMinutes(-5),
                now.AddSeconds(-3), TimeSpan.FromMinutes(5), 216_000_000, 12_000_000,
                420_000, 27_000, 3, true),
            new(DemoDevices[3].MacKey, "steam", "Steam", now.AddMinutes(-22),
                now.AddSeconds(-8), TimeSpan.FromMinutes(22), 4_760_000_000, 42_000_000,
                510_000, 8_400, 8, true),
        ];
        var identities = DemoDevices.ToDictionary(
            seed => seed.MacKey,
            seed => new ServiceDeviceIdentity(seed.Name, seed.IpAddress),
            StringComparer.OrdinalIgnoreCase);
        var groups = ServiceInspectorPresentationBuilder.Build(
            snapshots,
            identities,
            new ServiceUsageHistory(),
            now,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        ServiceDeviceGroups.Clear();
        foreach (var group in groups)
        {
            ServiceDeviceGroups.Add(group);
        }

        ServiceInspectorList.IsVisible = true;
        ServiceInspectorEmptyState.IsVisible = false;
        ServiceInspectorCountText.Text = $"{groups.Count} devices  •  {groups.Sum(group => group.Services.Count)} services";
    }

    private static DeviceSnapshot CreateDemoSnapshot(
        DemoDeviceSeed seed,
        DateTimeOffset now,
        double download,
        double upload) =>
        new(
            PhysicalAddress.Parse(seed.MacKey),
            IPAddress.Parse(seed.IpAddress),
            seed.Name,
            now.AddMinutes(-38),
            now,
            download,
            upload);

    private static DeviceTrafficSnapshot[] CreateDemoTraffic(int phase)
    {
        var traffic = new DeviceTrafficSnapshot[DemoDevices.Length];
        for (var index = 0; index < DemoDevices.Length; index++)
        {
            var seed = DemoDevices[index];
            var downloadWave = 0.68 + 0.32 * Math.Abs(Math.Sin((phase + index * 13) / 11d));
            var uploadWave = 0.58 + 0.42 * Math.Abs(Math.Cos((phase + index * 9) / 8d));
            traffic[index] = new DeviceTrafficSnapshot(
                seed.Name,
                seed.Download * downloadWave,
                seed.Upload * uploadWave);
        }

        return traffic;
    }

    private sealed record DemoDeviceSeed(
        string Name,
        string IpAddress,
        string MacKey,
        double Download,
        double Upload,
        int DownloadLimit,
        int UploadLimit,
        bool Paused);
}
