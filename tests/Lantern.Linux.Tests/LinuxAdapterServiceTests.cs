using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Networking;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxAdapterServiceTests
{
    [Fact]
    public void BuildProfiles_MapsWirelessInterfaceTypeToWifi()
    {
        var snapshot = new LinuxInterfaceSnapshot(
            "wlan0",
            "Wi-Fi",
            "Wireless",
            true,
            PhysicalAddress.Parse("001122334455"),
            [(IPAddress.Parse("192.168.31.20"), 24)],
            [IPAddress.Parse("192.168.31.1")]) with
        {
            InterfaceType = NetworkInterfaceType.Wireless80211,
        };

        var profile = Assert.Single(LinuxAdapterService.BuildProfiles([snapshot]));

        Assert.Equal(AdapterConnectionKind.Wifi, profile.ConnectionKind);
    }
    [Fact]
    public void BuildProfiles_ReturnsOnlyUsableIpv4InterfacesWithGateways()
    {
        var snapshots = new[]
        {
            new LinuxInterfaceSnapshot(
                "eth0",
                "Ethernet",
                "Intel I225",
                true,
                PhysicalAddress.Parse("345A6063C052"),
                [(IPAddress.Parse("192.168.31.247"), 24)],
                [IPAddress.Parse("192.168.31.1")]),
            new LinuxInterfaceSnapshot(
                "lo",
                "Loopback",
                "Loopback",
                true,
                PhysicalAddress.None,
                [(IPAddress.Loopback, 8)],
                []),
            new LinuxInterfaceSnapshot(
                "wlan0",
                "Wi-Fi",
                "Wireless",
                false,
                PhysicalAddress.Parse("001122334455"),
                [(IPAddress.Parse("10.0.0.20"), 24)],
                [IPAddress.Parse("10.0.0.1")]),
        };

        var profile = Assert.Single(LinuxAdapterService.BuildProfiles(snapshots));

        Assert.Equal("eth0", profile.Id);
        Assert.Equal(IPAddress.Parse("192.168.31.247"), profile.LocalAddress);
        Assert.Equal(IPAddress.Parse("192.168.31.1"), profile.GatewayAddress);
        Assert.Equal(24, profile.PrefixLength);
    }
}
