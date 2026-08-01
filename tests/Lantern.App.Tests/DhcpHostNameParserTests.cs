using System.Net.NetworkInformation;
using System.Text;
using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DhcpHostNameParserTests
{
    [Fact]
    public void TryParse_ReadsClientHostNameAndHardwareAddress()
    {
        var packet = BuildDhcpRequest("POCO-F6", "0E4F69CCE4F0");

        var parsed = DhcpHostNameParser.TryParse(packet, out var result);

        Assert.True(parsed);
        Assert.Equal("POCO-F6", result.HostName);
        Assert.Equal(PhysicalAddress.Parse("0E4F69CCE4F0"), result.MacAddress);
    }

    [Fact]
    public void TryParse_RejectsOrdinaryUdpTraffic()
    {
        Assert.False(DhcpHostNameParser.TryParse(new byte[64], out _));
    }

    private static byte[] BuildDhcpRequest(string hostName, string macAddress)
    {
        var name = Encoding.ASCII.GetBytes(hostName);
        var dhcpLength = 240 + 2 + name.Length + 1;
        var packet = new byte[14 + 20 + 8 + dhcpLength];
        var mac = PhysicalAddress.Parse(macAddress).GetAddressBytes();
        Array.Fill(packet, (byte)0xff, 0, 6);
        mac.CopyTo(packet, 6);
        packet[12] = 0x08;
        packet[13] = 0x00;

        var ip = 14;
        packet[ip] = 0x45;
        var networkLength = 20 + 8 + dhcpLength;
        packet[ip + 2] = (byte)(networkLength >> 8);
        packet[ip + 3] = (byte)networkLength;
        packet[ip + 9] = 17;
        packet[ip + 16] = 255;
        packet[ip + 17] = 255;
        packet[ip + 18] = 255;
        packet[ip + 19] = 255;

        var udp = ip + 20;
        packet[udp] = 0x00;
        packet[udp + 1] = 68;
        packet[udp + 2] = 0x00;
        packet[udp + 3] = 67;
        var udpLength = 8 + dhcpLength;
        packet[udp + 4] = (byte)(udpLength >> 8);
        packet[udp + 5] = (byte)udpLength;

        var dhcp = udp + 8;
        packet[dhcp] = 1;
        packet[dhcp + 1] = 1;
        packet[dhcp + 2] = 6;
        mac.CopyTo(packet, dhcp + 28);
        packet[dhcp + 236] = 99;
        packet[dhcp + 237] = 130;
        packet[dhcp + 238] = 83;
        packet[dhcp + 239] = 99;
        packet[dhcp + 240] = 12;
        packet[dhcp + 241] = (byte)name.Length;
        name.CopyTo(packet, dhcp + 242);
        packet[dhcp + 242 + name.Length] = 255;
        return packet;
    }
}
