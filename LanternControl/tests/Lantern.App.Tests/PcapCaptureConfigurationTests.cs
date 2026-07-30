using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class PcapCaptureConfigurationTests
{
    [Fact]
    public void CreateForForwarding_UsesImmediatePacketDelivery()
    {
        var configuration = PcapCaptureConfiguration.CreateForForwarding();

        Assert.True(configuration.Immediate);
        Assert.InRange(configuration.ReadTimeout, 0, 1);
        Assert.Equal(0, configuration.MinToCopy);
    }
}
