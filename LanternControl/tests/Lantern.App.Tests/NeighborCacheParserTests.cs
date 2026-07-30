using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class NeighborCacheParserTests
{
    [Fact]
    public void Parse_ReturnsOnlyUnicastNeighborsFromTheSelectedSubnet()
    {
        const string output = """
            Interface: 192.168.31.247 --- 0x13
              Internet Address      Physical Address      Type
              192.168.31.1          64-64-4a-38-0a-15     dynamic
              192.168.31.61         e2-61-19-0d-bd-54     dynamic
              192.168.31.247        34-5a-60-63-c0-52     dynamic
              192.168.31.255        ff-ff-ff-ff-ff-ff     static
              224.0.0.251           01-00-5e-00-00-fb     static
              192.168.100.10        10-20-30-40-50-60     dynamic
            """;

        var neighbors = NeighborCacheParser.Parse(
            output,
            IPAddress.Parse("192.168.31.247"),
            24);

        Assert.Equal(2, neighbors.Count);
        Assert.Equal(IPAddress.Parse("192.168.31.1"), neighbors[0].Address);
        Assert.Equal(PhysicalAddress.Parse("64644A380A15"), neighbors[0].MacAddress);
        Assert.Equal(IPAddress.Parse("192.168.31.61"), neighbors[1].Address);
        Assert.Equal(PhysicalAddress.Parse("E261190DBD54"), neighbors[1].MacAddress);
    }

    [Fact]
    public void Parse_AcceptsColonSeparatedMacAddressesWithoutSendingDiscoveryTraffic()
    {
        const string output = "192.168.50.8  aa:bb:cc:dd:ee:ff  reachable";

        var neighbors = NeighborCacheParser.Parse(
            output,
            IPAddress.Parse("192.168.50.2"),
            24);

        var neighbor = Assert.Single(neighbors);
        Assert.Equal(IPAddress.Parse("192.168.50.8"), neighbor.Address);
        Assert.Equal(PhysicalAddress.Parse("AABBCCDDEEFF"), neighbor.MacAddress);
    }
}
