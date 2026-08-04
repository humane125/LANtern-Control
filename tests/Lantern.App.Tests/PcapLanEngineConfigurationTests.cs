using Lantern.App.Services;
using Lantern.Core.Control;
using Lantern.Core.Devices;
using Lantern.Core.Services;
using Lantern.Core.Settings;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

namespace Lantern.App.Tests;

public sealed class PcapLanEngineConfigurationTests
{
    [Fact]
    public void Constructor_ExposesInjectedServiceInspectorTracker()
    {
        var tracker = new ServiceInspectorTracker();
        var engine = new PcapLanEngine(new DeviceRegistry(), new TrafficPolicy(), tracker);

        Assert.Same(tracker, engine.ServiceInspector);
    }

    [Fact]
    public void KnownDeviceHint_CarriesPersistedDhcpHostName()
    {
        var hint = new KnownDeviceHint(
            PhysicalAddress.Parse("0E4F69CCE4F0"),
            IPAddress.Parse("192.168.31.213"),
            "POCO-F6");

        Assert.Equal("POCO-F6", hint.HostName);
    }

    [Fact]
    public void ProbeInterval_MatchesThePreviouslyWorkingScanner()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(10), PcapLanEngine.ProbeInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(800), PcapLanEngine.ProbeReplyWindow);
    }

    [Fact]
    public void AutomaticMaintenance_DoesNotContinuouslySweepTheSubnet()
    {
        Assert.False(PassiveDiscoveryProfile.Default.ProbeSubnetOnRefresh);
        Assert.Equal(TimeSpan.FromSeconds(5), PassiveDiscoveryProfile.Default.RefreshInterval);
    }

    [Fact]
    public void Engine_ReportsRunningStateChangesToTheWindow()
    {
        Assert.NotNull(typeof(PcapLanEngine).GetEvent("StateChanged"));
    }

    [Fact]
    public void KnownDeviceHints_SuppressAmbiguousLearnedNamesButKeepAliases()
    {
        var settings = new AppSettings
        {
            Devices =
            {
                ["0E4F69CCE4F0"] = new DevicePreferences
                {
                    Alias = "My phone",
                    LearnedHostName = "Humane",
                    LastKnownIp = "192.168.31.213",
                },
                ["D2574CDCA5B2"] = new DevicePreferences
                {
                    LearnedHostName = "Humane",
                    LastKnownIp = "192.168.31.225",
                },
            },
        };

        var hints = KnownDeviceHintFactory.Build(settings);

        Assert.Equal("My phone", hints.Single(hint =>
            hint.MacAddress.Equals(PhysicalAddress.Parse("0E4F69CCE4F0"))).HostName);
        Assert.Null(hints.Single(hint =>
            hint.MacAddress.Equals(PhysicalAddress.Parse("D2574CDCA5B2"))).HostName);
    }

    [Fact]
    public void ResolvedDeviceNames_RejectControllerAndCrossDeviceDuplicates()
    {
        var claims = new ResolvedDeviceNameClaims("Humane");
        var firstMac = PhysicalAddress.Parse("0E4F69CCE4F0");
        var secondMac = PhysicalAddress.Parse("D2574CDCA5B2");

        Assert.False(claims.TryClaim(firstMac, "Humane", out _));
        Assert.True(claims.TryClaim(firstMac, "POCO-F6", out var accepted));
        Assert.Equal("POCO-F6", accepted);
        Assert.False(claims.TryClaim(secondMac, "POCO-F6", out _));
    }

    [Fact]
    public void ResolvedDeviceNames_ReportOnlyTheFirstAcceptedClaimAsNew()
    {
        var claims = new ResolvedDeviceNameClaims("Humane");
        var mac = PhysicalAddress.Parse("0E4F69CCE4F0");

        Assert.True(claims.TryClaim(mac, "POCO-F6", out var accepted, out var firstClaim));
        Assert.Equal("POCO-F6", accepted);
        Assert.True(firstClaim);

        Assert.True(claims.TryClaim(mac, "POCO-F6", out accepted, out var repeatedClaim));
        Assert.Equal("POCO-F6", accepted);
        Assert.False(repeatedClaim);
    }
}
