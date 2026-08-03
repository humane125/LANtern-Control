using Lantern.Linux.Services;
using SharpPcap;
using System.Net.NetworkInformation;

namespace Lantern.Linux.Tests;

public sealed class LinuxCaptureConfigurationTests
{
    [Fact]
    public void Create_UsesImmediateLowLatencyCaptureForForwarding()
    {
        var configuration = LinuxCaptureConfiguration.CreateForForwarding();

        Assert.Equal(DeviceModes.None, configuration.Mode);
        Assert.Equal(1, configuration.ReadTimeout);
        Assert.True(configuration.Immediate);
        Assert.True(configuration.Snaplen >= 65_535);
        Assert.Equal(8 * 1024 * 1024, configuration.BufferSize);
        Assert.Null(configuration.KernelBufferSize);
        Assert.Null(configuration.MinToCopy);
    }

    [Fact]
    public void ForwardingFilter_CapturesOnlyFramesAddressedToController()
    {
        var localMac = PhysicalAddress.Parse("027700000002");

        var filter = LinuxCaptureFilter.ForForwarding(localMac);

        Assert.Equal("ip and ether dst 02:77:00:00:00:02", filter);
    }
}
