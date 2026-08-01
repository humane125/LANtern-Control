using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class FrameRouterTests
{
    private static readonly PhysicalAddress LocalMac = PhysicalAddress.Parse("345A6063C052");
    private static readonly PhysicalAddress GatewayMac = PhysicalAddress.Parse("64644A380A15");
    private static readonly PhysicalAddress ClientMac = PhysicalAddress.Parse("E261190DBD54");
    private static readonly IPAddress LocalIp = IPAddress.Parse("192.168.31.247");
    private static readonly IPAddress ClientIp = IPAddress.Parse("192.168.31.61");

    [Fact]
    public void Route_UploadRewritesFrameTowardGateway()
    {
        var frame = BuildIpv4Frame(
            destinationMac: LocalMac,
            sourceMac: ClientMac,
            sourceIp: ClientIp,
            destinationIp: IPAddress.Parse("1.1.1.1"),
            payloadBytes: 100);
        var router = CreateRouter();

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Forward, result.Action);
        Assert.Equal(TrafficDirection.Upload, result.Direction);
        Assert.Equal(ClientMac, result.ClientMac);
        Assert.Equal(GatewayMac.GetAddressBytes(), result.Frame![..6]);
        Assert.Equal(LocalMac.GetAddressBytes(), result.Frame[6..12]);
    }

    [Fact]
    public void Route_DownloadRewritesFrameTowardClient()
    {
        var frame = BuildIpv4Frame(
            destinationMac: LocalMac,
            sourceMac: GatewayMac,
            sourceIp: IPAddress.Parse("1.1.1.1"),
            destinationIp: ClientIp,
            payloadBytes: 100);
        var router = CreateRouter();

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Forward, result.Action);
        Assert.Equal(TrafficDirection.Download, result.Direction);
        Assert.Equal(ClientMac.GetAddressBytes(), result.Frame![..6]);
        Assert.Equal(LocalMac.GetAddressBytes(), result.Frame[6..12]);
    }

    [Fact]
    public void Route_PausedClientDropsFrame()
    {
        var policy = new TrafficPolicy();
        policy.SetRule(ClientMac.ToString(), new TrafficRule(true, 0, 0));
        var router = CreateRouter(policy);
        var frame = BuildIpv4Frame(
            LocalMac,
            ClientMac,
            ClientIp,
            IPAddress.Parse("8.8.8.8"),
            40);

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Drop, result.Action);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void Route_LocalComputerTrafficIsIgnored()
    {
        var router = CreateRouter();
        var frame = BuildIpv4Frame(
            GatewayMac,
            LocalMac,
            LocalIp,
            IPAddress.Parse("8.8.8.8"),
            40);

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Ignore, result.Action);
    }

    private static FrameRouter CreateRouter(TrafficPolicy? policy = null) =>
        new(
            LocalMac,
            LocalIp,
            GatewayMac,
            new Dictionary<IPAddress, PhysicalAddress> { [ClientIp] = ClientMac },
            policy ?? new TrafficPolicy());

    private static byte[] BuildIpv4Frame(
        PhysicalAddress destinationMac,
        PhysicalAddress sourceMac,
        IPAddress sourceIp,
        IPAddress destinationIp,
        int payloadBytes)
    {
        var frame = new byte[14 + 20 + payloadBytes];
        destinationMac.GetAddressBytes().CopyTo(frame, 0);
        sourceMac.GetAddressBytes().CopyTo(frame, 6);
        frame[12] = 0x08;
        frame[13] = 0x00;
        frame[14] = 0x45;
        var totalLength = 20 + payloadBytes;
        frame[16] = (byte)(totalLength >> 8);
        frame[17] = (byte)totalLength;
        frame[22] = 64;
        frame[23] = 6;
        sourceIp.GetAddressBytes().CopyTo(frame, 26);
        destinationIp.GetAddressBytes().CopyTo(frame, 30);
        return frame;
    }
}
