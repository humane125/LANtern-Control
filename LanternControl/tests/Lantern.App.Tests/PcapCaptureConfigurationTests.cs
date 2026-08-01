using Lantern.App.Services;
using SharpPcap;
using Xunit;

namespace Lantern.App.Tests;

public sealed class PcapCaptureConfigurationTests
{
    [Fact]
    public void ArpDiscovery_UsesPromiscuousCaptureLikeTheWorkingScanner()
    {
        var configuration = PcapCaptureConfiguration.CreateForArpDiscovery();

        Assert.Equal(DeviceModes.Promiscuous, configuration.Mode);
        Assert.True(configuration.Immediate);
    }

    [Fact]
    public void CreateForForwarding_UsesImmediatePacketDelivery()
    {
        var configuration = PcapCaptureConfiguration.CreateForForwarding();

        Assert.Equal(DeviceModes.None, configuration.Mode);
        Assert.True(configuration.Immediate);
        Assert.InRange(configuration.ReadTimeout, 0, 1);
        Assert.Equal(0, configuration.MinToCopy);
    }
}
