using Lantern.App.ViewModels;
using Lantern.Core.Services;
using Lantern.Core.Settings;
using Xunit;

namespace Lantern.App.Tests;

public sealed class ServiceInspectorPresentationTests
{
    [Fact]
    public void BuildRememberedIdentities_UsesSavedNamesForDevicesMissingFromTheLiveList()
    {
        var settings = new AppSettings();
        settings.Devices["0E4F69CCE4F0"] = new DevicePreferences
        {
            LearnedHostName = "POCO-F6",
            LastKnownIp = "192.168.31.213",
        };
        settings.Devices["D2574CDCA5B2"] = new DevicePreferences
        {
            Alias = "Pixel-10-Pro",
            LearnedHostName = "Android",
            LastKnownIp = "192.168.31.225",
        };

        var identities = ServiceInspectorPresentationBuilder.BuildRememberedIdentities(settings);

        Assert.Equal("POCO-F6", identities["0E4F69CCE4F0"].DeviceName);
        Assert.Equal("192.168.31.213", identities["0E4F69CCE4F0"].IpAddress);
        Assert.Equal("Pixel-10-Pro", identities["D2574CDCA5B2"].DeviceName);
        Assert.Equal("192.168.31.225", identities["D2574CDCA5B2"].IpAddress);
    }

    [Fact]
    public void BuildRememberedIdentities_DoesNotReuseAnAmbiguousLearnedName()
    {
        var settings = new AppSettings();
        settings.Devices["0E4F69CCE4F0"] = new DevicePreferences
        {
            LearnedHostName = "Humane",
            LastKnownIp = "192.168.31.213",
        };
        settings.Devices["D2574CDCA5B2"] = new DevicePreferences
        {
            LearnedHostName = "Humane",
            LastKnownIp = "192.168.31.225",
        };

        var identities = ServiceInspectorPresentationBuilder.BuildRememberedIdentities(settings);

        Assert.Empty(identities);
    }

    [Fact]
    public void Build_GroupsSessionsByDeviceAndFormatsLiveAndDailyMetrics()
    {
        var now = new DateTimeOffset(2026, 8, 4, 12, 5, 0, TimeSpan.Zero);
        var snapshot = new ServiceSessionSnapshot(
            "0E4F69CCE4F0",
            "youtube",
            "YouTube",
            now.AddMinutes(-5),
            now.AddSeconds(-2),
            TimeSpan.FromMinutes(5),
            4_000,
            1_000,
            1_600,
            400,
            2,
            true);
        var history = new ServiceUsageHistory
        {
            Days =
            [
                new ServiceUsageDay
                {
                    Date = DateOnly.FromDateTime(now.LocalDateTime),
                    Services =
                    [
                        new ServiceUsageAggregate
                        {
                            MacKey = "0E4F69CCE4F0",
                            ServiceId = "youtube",
                            ServiceName = "YouTube",
                            DownloadBytes = 10_000,
                            UploadBytes = 2_000,
                            ActiveDuration = TimeSpan.FromMinutes(8),
                            SessionCount = 2,
                            LastActivity = now.AddMinutes(-10),
                        },
                    ],
                },
            ],
        };
        var identities = new Dictionary<string, ServiceDeviceIdentity>
        {
            ["0E4F69CCE4F0"] = new("POCO-F6", "192.168.31.213"),
        };

        var groups = ServiceInspectorPresentationBuilder.Build(
            [snapshot],
            identities,
            history,
            now,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0E4F69CCE4F0" });

        var group = Assert.Single(groups);
        Assert.Equal("POCO-F6", group.DeviceName);
        Assert.Equal("192.168.31.213", group.IpAddress);
        Assert.True(group.IsExpanded);
        var service = Assert.Single(group.Services);
        Assert.Equal("YouTube", service.ServiceName);
        Assert.Equal("1.6 KB/s", service.DownloadRateText);
        Assert.Equal("400 B/s", service.UploadRateText);
        Assert.Equal("4.0 KB", service.SessionDownloadText);
        Assert.Equal("1.0 KB", service.SessionUploadText);
        Assert.Equal("14.0 KB", service.TodayDownloadText);
        Assert.Equal("3.0 KB", service.TodayUploadText);
        Assert.Equal("5m 00s", service.DurationText);
        Assert.Equal("2 connections", service.ConnectionCountText);
        Assert.Equal("Active", service.StatusText);
    }

    [Fact]
    public void Build_UsesStableFallbackIdentityAndCollapsedDefault()
    {
        var now = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
        var snapshot = new ServiceSessionSnapshot(
            "E261190DBD54", "other", "Other", now, now, TimeSpan.Zero,
            0, 0, 0, 0, 1, true);

        var group = Assert.Single(ServiceInspectorPresentationBuilder.Build(
            [snapshot],
            new Dictionary<string, ServiceDeviceIdentity>(),
            new ServiceUsageHistory(),
            now,
            new HashSet<string>()));

        Assert.Equal("Device E2:61:19:0D:BD:54", group.DeviceName);
        Assert.Equal("-", group.IpAddress);
        Assert.False(group.IsExpanded);
    }

    [Fact]
    public void Build_HistoryOnlyServiceShowsPersistedDuration()
    {
        var now = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
        var history = new ServiceUsageHistory
        {
            Days =
            [
                new ServiceUsageDay
                {
                    Date = DateOnly.FromDateTime(now.LocalDateTime),
                    Services =
                    [
                        new ServiceUsageAggregate
                        {
                            MacKey = "E261190DBD54",
                            ServiceId = "spotify",
                            ServiceName = "Spotify",
                            ActiveDuration = TimeSpan.FromMinutes(42),
                            LastActivity = now.AddMinutes(-2),
                        },
                    ],
                },
            ],
        };

        var service = Assert.Single(Assert.Single(ServiceInspectorPresentationBuilder.Build(
            [],
            new Dictionary<string, ServiceDeviceIdentity>(),
            history,
            now,
            new HashSet<string>())).Services);

        Assert.Equal("42m 00s", service.DurationText);
        Assert.Equal("Idle", service.StatusText);
    }
}
