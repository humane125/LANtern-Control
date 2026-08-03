using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class ArpInterceptionFramesTests
{
    [Fact]
    public void ControllerProtectionFrame_IsNotAnAuthenticClientObservation()
    {
        var controllerMac = PhysicalAddress.Parse("345A6063C052");
        var gatewayMac = PhysicalAddress.Parse("64644A380A15");
        var clientMac = PhysicalAddress.Parse("0E4F69CCE4F0");
        var frames = ArpInterceptionFrames.BuildControllerProtection(
            controllerMac,
            IPAddress.Parse("192.168.31.247"),
            gatewayMac,
            IPAddress.Parse("192.168.31.1"),
            clientMac,
            IPAddress.Parse("192.168.31.213"));

        Assert.True(EthernetFrameCodec.TryParseArp(frames.ClientToController, out var arp));
        Assert.Equal(controllerMac, arp.EthernetSourceMac);
        Assert.Equal(clientMac, arp.SenderMac);
        Assert.False(arp.HasConsistentSender);
    }

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
    public void BuildRecoveryRequest_MakesTheClientRevalidateWithTheRealGateway()
    {
        var frame = ArpInterceptionFrames.BuildRecoveryRequest(
            LocalMac,
            GatewayMac,
            GatewayIp,
            ClientMac,
            ClientIp);

        Assert.Equal(ClientMac.GetAddressBytes(), frame[..6]);
        Assert.Equal(LocalMac.GetAddressBytes(), frame[6..12]);
        Assert.True(EthernetFrameCodec.TryParseArp(frame, out var request));
        Assert.False(request.HasConsistentSender);
        Assert.Equal(ArpOperation.Request, request.Operation);
        Assert.Equal(GatewayMac, request.SenderMac);
        Assert.Equal(GatewayIp, request.SenderIp);
        Assert.Equal(ClientMac, request.TargetMac);
        Assert.Equal(ClientIp, request.TargetIp);
    }

    [Fact]
    public void BuildRestore_AdvertisesTheRealPeersToBothSides()
    {
        var frames = ArpInterceptionFrames.BuildRestore(
            LocalMac,
            GatewayIp,
            GatewayMac,
            ClientIp,
            ClientMac);

        Assert.True(EthernetFrameCodec.TryParseArp(frames.ToGateway, out var toGateway));
        Assert.Equal(ClientMac, toGateway.EthernetSourceMac);
        Assert.True(toGateway.HasConsistentSender);
        Assert.Equal(ClientMac, toGateway.SenderMac);
        Assert.Equal(ClientIp, toGateway.SenderIp);
        Assert.Equal(GatewayMac, toGateway.TargetMac);
        Assert.Equal(GatewayIp, toGateway.TargetIp);

        Assert.True(EthernetFrameCodec.TryParseArp(frames.ToClient, out var toClient));
        Assert.Equal(LocalMac, toClient.EthernetSourceMac);
        Assert.False(toClient.HasConsistentSender);
        Assert.Equal(GatewayMac, toClient.SenderMac);
        Assert.Equal(GatewayIp, toClient.SenderIp);
        Assert.Equal(ClientMac, toClient.TargetMac);
        Assert.Equal(ClientIp, toClient.TargetIp);
    }

    [Fact]
    public void Select_ClientTargetOmitsTheGatewayPoisonFrame()
    {
        var frames = ArpInterceptionFrames.BuildPoison(
            LocalMac,
            GatewayIp,
            GatewayMac,
            ClientIp,
            ClientMac);

        var selected = frames.Select(InterceptionTargets.Client);

        Assert.Single(selected);
        Assert.Equal(frames.ToClient, selected[0]);
    }

    [Fact]
    public void Select_BothTargetsReturnsBothPeerFrames()
    {
        var frames = ArpInterceptionFrames.BuildPoison(
            LocalMac,
            GatewayIp,
            GatewayMac,
            ClientIp,
            ClientMac);

        var selected = frames.Select(
            InterceptionTargets.Client | InterceptionTargets.Gateway);

        Assert.Equal(2, selected.Count);
        Assert.Equal(frames.ToClient, selected[0]);
        Assert.Equal(frames.ToGateway, selected[1]);
    }

    [Fact]
    public void BuildControllerProtection_KeepsTheLocalGatewayMappingReal()
    {
        var controllerIp = IPAddress.Parse("192.168.31.247");

        var frames = ArpInterceptionFrames.BuildControllerProtection(
            LocalMac,
            controllerIp,
            GatewayMac,
            GatewayIp,
            ClientMac,
            ClientIp);

        Assert.Equal(LocalMac.GetAddressBytes(), frames.GatewayToController[..6]);
        Assert.Equal(GatewayMac.GetAddressBytes(), frames.GatewayToController[6..12]);
        Assert.True(
            EthernetFrameCodec.TryParseArp(
                frames.GatewayToController,
                out var gatewayReply));
        Assert.Equal(GatewayMac, gatewayReply.SenderMac);
        Assert.Equal(GatewayIp, gatewayReply.SenderIp);
        Assert.Equal(LocalMac, gatewayReply.TargetMac);
        Assert.Equal(controllerIp, gatewayReply.TargetIp);
    }

    [Fact]
    public void BuildControllerProtection_KeepsTheLocalClientMappingReal()
    {
        var controllerIp = IPAddress.Parse("192.168.31.247");

        var frames = ArpInterceptionFrames.BuildControllerProtection(
            LocalMac,
            controllerIp,
            GatewayMac,
            GatewayIp,
            ClientMac,
            ClientIp);

        Assert.Equal(LocalMac.GetAddressBytes(), frames.ClientToController[..6]);
        Assert.Equal(LocalMac.GetAddressBytes(), frames.ClientToController[6..12]);
        Assert.True(
            EthernetFrameCodec.TryParseArp(
                frames.ClientToController,
                out var clientReply));
        Assert.Equal(ClientMac, clientReply.SenderMac);
        Assert.Equal(ClientIp, clientReply.SenderIp);
        Assert.Equal(LocalMac, clientReply.TargetMac);
        Assert.Equal(controllerIp, clientReply.TargetIp);
    }
}
