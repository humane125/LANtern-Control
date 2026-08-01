using Lantern.App.Services;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

namespace Lantern.App.Tests;

public sealed class PcapLanEngineConfigurationTests
{
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
}
