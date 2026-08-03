using Lantern.Core.Control;
using Lantern.Core.Settings;
using Lantern.Linux.ViewModels;

namespace Lantern.Linux.Tests;

public sealed class LinuxDashboardStateTests
{
    [Fact]
    public void ApplyPreset_AccumulatesPersistentRulesForOnlyTheSelectedDevice()
    {
        var settings = new AppSettings();
        var policy = new TrafficPolicy();
        var state = new LinuxDashboardState(settings, policy);

        state.ApplyPreset("0E:4F:69:CC:E4:F0", DomainBlockPresetCatalog.All[1]);
        state.ApplyPreset("0E:4F:69:CC:E4:F0", DomainBlockPresetCatalog.All[2]);

        var saved = settings.BlockedDomains["0E4F69CCE4F0"];
        Assert.Contains("instagram.com", saved);
        Assert.Contains("facebook.com", saved);
        Assert.DoesNotContain("youtube.com", saved);
        Assert.True(policy.ShouldBlockDomain("0E4F69CCE4F0", "graph.facebook.com"));
        Assert.False(policy.ShouldBlockDomain("AA7051600355", "graph.facebook.com"));
    }

    [Fact]
    public void ApplyPreset_RecordsTheNamedPresetForGroupedPresentation()
    {
        var settings = new AppSettings();
        var state = new LinuxDashboardState(settings, new TrafficPolicy());
        var preset = DomainBlockPresetCatalog.All.Single(candidate => candidate.Name == "YouTube");

        state.ApplyPreset("0E:4F:69:CC:E4:F0", preset);
        state.ApplyPreset("0E:4F:69:CC:E4:F0", preset);

        Assert.Equal(["YouTube"], settings.AppliedDomainPresets["0E4F69CCE4F0"]);
    }

    [Fact]
    public void ApplyTrafficRule_PersistsUnlimitedZeroAndPauseState()
    {
        var settings = new AppSettings();
        var policy = new TrafficPolicy();
        var state = new LinuxDashboardState(settings, policy);

        state.ApplyTrafficRule("0E4F69CCE4F0", new TrafficRule(true, 0, 125));

        var saved = settings.Devices["0E4F69CCE4F0"];
        Assert.Equal(0, saved.DownloadKiloBytesPerSecond);
        Assert.Equal(125, saved.UploadKiloBytesPerSecond);
        Assert.True(saved.PauseInternet);
        Assert.Equal(new TrafficRule(true, 0, 125), policy.GetRule("0E4F69CCE4F0"));
    }

    [Fact]
    public void RemoveDomain_UpdatesPersistenceAndLivePolicy()
    {
        var settings = new AppSettings();
        var policy = new TrafficPolicy();
        var state = new LinuxDashboardState(settings, policy);
        state.ApplyPreset("0E4F69CCE4F0", DomainBlockPresetCatalog.All[2]);

        state.RemoveDomain("0E4F69CCE4F0", "facebook.com");

        Assert.DoesNotContain("facebook.com", settings.BlockedDomains["0E4F69CCE4F0"]);
        Assert.False(policy.ShouldBlockDomain("0E4F69CCE4F0", "graph.facebook.com"));
        Assert.True(policy.ShouldBlockDomain("0E4F69CCE4F0", "static.fbcdn.net"));
    }
}
