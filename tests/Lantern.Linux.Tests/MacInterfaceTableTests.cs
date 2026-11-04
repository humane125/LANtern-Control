using System.Net.NetworkInformation;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class MacInterfaceTableTests
{
    [Fact]
    public void Parse_MapsEachInterfaceToItsEtherAddress()
    {
        const string output = """
            lo0: flags=8049<UP,LOOPBACK,RUNNING,MULTICAST> mtu 16384
            	inet 127.0.0.1 netmask 0xff000000
            en0: flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST> mtu 1500
            	options=6460<TSO4,TSO6,CHANNEL_IO>
            	ether 1a:2b:3c:4d:5e:6f
            	inet 192.168.1.50 netmask 0xffffff00 broadcast 192.168.1.255
            en1: flags=8863<UP,BROADCAST> mtu 1500
            	ether 12:34:5:67:8:9a
            """;

        var macs = MacInterfaceTable.Parse(output);

        Assert.Equal(PhysicalAddress.Parse("1A2B3C4D5E6F"), macs["en0"]);
        Assert.Equal(PhysicalAddress.Parse("12340567089A"), macs["en1"]);
        Assert.False(macs.ContainsKey("lo0"));
    }
}
