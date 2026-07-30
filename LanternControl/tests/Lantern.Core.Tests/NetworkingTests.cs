using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class NetworkingTests
{
    [Fact]
    public void EnumerateHosts_ReturnsUsableAddressesForSlash30()
    {
        var hosts = SubnetScanner.EnumerateHosts(IPAddress.Parse("192.168.50.2"), 30);

        Assert.Equal(
            new[] { IPAddress.Parse("192.168.50.1"), IPAddress.Parse("192.168.50.2") },
            hosts);
    }

    [Fact]
    public void EnumerateHosts_ExcludesNetworkAndBroadcastForSlash24()
    {
        var hosts = SubnetScanner.EnumerateHosts(IPAddress.Parse("192.168.31.247"), 24);

        Assert.Equal(254, hosts.Count);
        Assert.Equal(IPAddress.Parse("192.168.31.1"), hosts[0]);
        Assert.Equal(IPAddress.Parse("192.168.31.254"), hosts[^1]);
    }

    [Fact]
    public void EnumerateHosts_RejectsNetworksLargerThanSafetyLimit()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => SubnetScanner.EnumerateHosts(IPAddress.Parse("10.0.0.1"), 21));

        Assert.Contains("1024", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArpRequest_ProducesExactEthernetFrame()
    {
        var bytes = EthernetFrameCodec.BuildArpRequest(
            PhysicalAddress.Parse("345A6063C052"),
            IPAddress.Parse("192.168.31.247"),
            IPAddress.Parse("192.168.31.1"));

        var expected = Convert.FromHexString(
            "FFFFFFFFFFFF345A6063C0520806" +
            "0001080006040001" +
            "345A6063C052C0A81FF7" +
            "000000000000C0A81F01");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void BuildArpReply_ProducesExactCorrectiveFrame()
    {
        var bytes = EthernetFrameCodec.BuildArpReply(
            PhysicalAddress.Parse("64644A380A15"),
            IPAddress.Parse("192.168.31.1"),
            PhysicalAddress.Parse("E261190DBD54"),
            IPAddress.Parse("192.168.31.61"));

        var expected = Convert.FromHexString(
            "E261190DBD5464644A380A150806" +
            "0001080006040002" +
            "64644A380A15C0A81F01" +
            "E261190DBD54C0A81F3D");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void TryParseArp_ReadsSenderAndTarget()
    {
        var frame = Convert.FromHexString(
            "345A6063C05264644A380A150806" +
            "0001080006040002" +
            "64644A380A15C0A81F01" +
            "345A6063C052C0A81FF7");

        var parsed = EthernetFrameCodec.TryParseArp(frame, out var arp);

        Assert.True(parsed);
        Assert.Equal(ArpOperation.Reply, arp.Operation);
        Assert.Equal(PhysicalAddress.Parse("64644A380A15"), arp.SenderMac);
        Assert.Equal(IPAddress.Parse("192.168.31.1"), arp.SenderIp);
        Assert.Equal(PhysicalAddress.Parse("345A6063C052"), arp.TargetMac);
        Assert.Equal(IPAddress.Parse("192.168.31.247"), arp.TargetIp);
    }

    [Fact]
    public void TryParseIpv4_SupportsSingleVlanTag()
    {
        var frame = new byte[14 + 4 + 20];
        Convert.FromHexString("345A6063C052E261190DBD54810000640800").CopyTo(frame, 0);
        frame[18] = 0x45;
        IPAddress.Parse("192.168.31.61").GetAddressBytes().CopyTo(frame, 30);
        IPAddress.Parse("1.1.1.1").GetAddressBytes().CopyTo(frame, 34);

        var parsed = EthernetFrameCodec.TryParseIpv4(frame, out var ipv4);

        Assert.True(parsed);
        Assert.Equal(18, ipv4.HeaderOffset);
        Assert.Equal(IPAddress.Parse("192.168.31.61"), ipv4.Source);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), ipv4.Destination);
    }
}
