using System.Net;
using System.Net.NetworkInformation;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class MacArpCacheTests
{
    [Fact]
    public void Parse_ReturnsCompleteNeighborsForSelectedInterface()
    {
        const string output = """
            ? (192.168.1.1) at 10:22:33:44:55:66 on en0 ifscope [ethernet]
            ? (192.168.1.7) at 1a:2b:3c:4d:5e:6f on en0 ifscope [ethernet]
            ? (192.168.1.9) at (incomplete) on en0 [ethernet]
            ? (192.168.1.20) at 2:1:3:4:5:6 on en1 ifscope [ethernet]
            ? (192.168.1.30) at 10:22:33:44:55:67 on
            """;

        var entries = MacArpCache.Parse(
            output,
            "en0",
            IPAddress.Parse("192.168.1.50"),
            24);

        Assert.Equal(2, entries.Count);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), entries[0].Address);
        Assert.Equal(PhysicalAddress.Parse("102233445566"), entries[0].MacAddress);
        Assert.Equal(IPAddress.Parse("192.168.1.7"), entries[1].Address);
        Assert.Equal(PhysicalAddress.Parse("1A2B3C4D5E6F"), entries[1].MacAddress);
    }

    [Fact]
    public void Parse_PadsSingleDigitOctetsThatMacArpPrints()
    {
        const string output = "? (192.168.1.8) at 12:34:5:67:8:9a on en0 ifscope [ethernet]";

        var entry = Assert.Single(MacArpCache.Parse(
            output,
            "en0",
            IPAddress.Parse("192.168.1.50"),
            24));

        Assert.Equal(PhysicalAddress.Parse("12340567089A"), entry.MacAddress);
    }

    [Fact]
    public void Parse_RejectsEntriesOutsideSelectedSubnet()
    {
        const string output = """
            ? (169.254.75.138) at 1a:2b:3c:4d:5e:70 on en0 [ethernet]
            ? (10.0.0.5) at 02:00:00:00:00:05 on en0 ifscope [ethernet]
            """;

        Assert.Empty(MacArpCache.Parse(
            output,
            "en0",
            IPAddress.Parse("192.168.1.50"),
            24));
    }
}
