using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class ArpInterceptionFramesTests
{
    private static readonly PhysicalAddress LocalMac = PhysicalAddress.Parse("345A6063C052");
    private static readonly PhysicalAddress GatewayMac = PhysicalAddress.Parse("64644A380A15");
    private static readonly PhysicalAddress ClientMac = PhysicalAddress.Parse("E261190DBD54");
    private static readonly IPAddress GatewayIp = IPAddress.Parse("192.168.31.1");
    private static readonly IPAddress ClientIp = IPAddress.Parse("192.168.31.61");

    [Fact]
    public void BuildPoison_AdvertisesControllerForBothPeers()
    {
        var frames = ArpInterceptionFrames.BuildPoison(
            LocalMac,
            GatewayIp,
            GatewayMac,
            ClientIp,
            ClientMac);

        Assert.True(EthernetFrameCodec.TryParseArp(frames.ToClient, out var toClient));
        Assert.Equal(LocalMac, toClient.SenderMac);
        Assert.Equal(GatewayIp, toClient.SenderIp);
        Assert.Equal(ClientMac, toClient.TargetMac);
        Assert.Equal(ClientIp, toClient.TargetIp);

        Assert.True(EthernetFrameCodec.TryParseArp(frames.ToGateway, out var toGateway));
        Assert.Equal(LocalMac, toGateway.SenderMac);
        Assert.Equal(ClientIp, toGateway.SenderIp);
        Assert.Equal(GatewayMac, toGateway.TargetMac);
        Assert.Equal(GatewayIp, toGateway.TargetIp);
    }

    [Fact]
    public void BuildRestore_AdvertisesRealPeerMacs()
    {
        var frames = ArpInterceptionFrames.BuildRestore(
            GatewayMac,
            GatewayIp,
            ClientMac,
            ClientIp);

        Assert.True(EthernetFrameCodec.TryParseArp(frames.ToClient, out var toClient));
        Assert.Equal(GatewayMac, toClient.SenderMac);
        Assert.True(EthernetFrameCodec.TryParseArp(frames.ToGateway, out var toGateway));
        Assert.Equal(ClientMac, toGateway.SenderMac);
    }
}
