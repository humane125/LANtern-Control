using System.Net;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class IPv4DiscoveryRangeTests
{
    [Fact]
    public void EnumerateHosts_UsesWholeSmallSubnetAndSkipsLocalAddress()
    {
        var hosts = IPv4DiscoveryRange.EnumerateHosts(
            IPAddress.Parse("192.168.31.246"),
            30);

        Assert.Equal(
            [IPAddress.Parse("192.168.31.245")],
            hosts);
    }

    [Fact]
    public void EnumerateHosts_CapsLargeNetworksToLocalSlash24()
    {
        var hosts = IPv4DiscoveryRange.EnumerateHosts(
            IPAddress.Parse("10.42.7.25"),
            16);

        Assert.Equal(253, hosts.Count);
        Assert.Equal(IPAddress.Parse("10.42.7.1"), hosts[0]);
        Assert.Equal(IPAddress.Parse("10.42.7.254"), hosts[^1]);
        Assert.DoesNotContain(IPAddress.Parse("10.42.7.25"), hosts);
    }
}
