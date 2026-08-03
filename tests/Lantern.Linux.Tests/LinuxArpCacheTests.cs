using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxArpCacheTests
{
    [Fact]
    public void Parse_ReturnsCompleteUnicastNeighborsForSelectedAdapter()
    {
        const string output = """
            IP address       HW type     Flags       HW address            Mask     Device
            192.168.31.1     0x1         0x2         64:64:4a:38:0a:15     *        wlp2s0
            192.168.31.10    0x1         0x2         f6:98:83:d2:00:f1     *        wlp2s0
            192.168.31.11    0x1         0x0         00:00:00:00:00:00     *        wlp2s0
            192.168.31.12    0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0
            192.168.31.255   0x1         0x2         ff:ff:ff:ff:ff:ff     *        wlp2s0
            """;

        var entries = LinuxArpCache.Parse(
            output,
            "wlp2s0",
            IPAddress.Parse("192.168.31.178"),
            24);

        Assert.Equal(2, entries.Count);
        Assert.Equal(IPAddress.Parse("192.168.31.1"), entries[0].Address);
        Assert.Equal(PhysicalAddress.Parse("64644A380A15"), entries[0].MacAddress);
        Assert.Equal(IPAddress.Parse("192.168.31.10"), entries[1].Address);
        Assert.Equal(PhysicalAddress.Parse("F69883D200F1"), entries[1].MacAddress);
    }

    [Fact]
    public void Parse_RejectsEntriesOutsideSelectedSubnetAndLocalAddress()
    {
        const string output = """
            IP address       HW type     Flags       HW address            Mask     Device
            192.168.31.178   0x1         0x2         5c:c0:ba:5a:61:05     *        wlp2s0
            192.168.50.20    0x1         0x2         02:00:00:00:00:20     *        wlp2s0
            """;

        Assert.Empty(LinuxArpCache.Parse(
            output,
            "wlp2s0",
            IPAddress.Parse("192.168.31.178"),
            24));
    }

    [Fact]
    public void ImportCachedNeighbors_RemembersRealMappingsButRejectsStaleControllerMappings()
    {
        var registry = new Lantern.Core.Devices.DeviceRegistry();
        var engine = new LinuxLanEngine(registry, new Lantern.Core.Control.TrafficPolicy());
        var profile = new Lantern.Core.Networking.AdapterProfile(
            "eno1",
            "eno1",
            "Ethernet",
            IPAddress.Parse("192.168.31.247"),
            24,
            IPAddress.Parse("192.168.31.1"),
            PhysicalAddress.Parse("345A6063C052"));
        var entries = new[]
        {
            new LinuxArpCacheEntry(
                IPAddress.Parse("192.168.31.213"),
                PhysicalAddress.Parse("0E4F69CCE4F0")),
            new LinuxArpCacheEntry(
                IPAddress.Parse("192.168.31.225"),
                profile.LocalMac),
        };
        var import = typeof(LinuxLanEngine).GetMethod(
            "ImportCachedNeighbors",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(import);
        var imported = Assert.IsType<int>(import.Invoke(engine, [profile, entries]));

        Assert.Equal(1, imported);
        var remembered = Assert.Single(registry.Peek());
        Assert.Equal(IPAddress.Parse("192.168.31.213"), remembered.IpAddress);
        Assert.Equal(DateTimeOffset.MinValue, remembered.LastSeen);
    }
}
