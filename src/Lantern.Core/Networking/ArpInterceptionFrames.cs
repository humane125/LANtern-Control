using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;

namespace Lantern.Core.Networking;

public sealed record ArpPeerFrames(byte[] ToClient, byte[] ToGateway)
{
    public IReadOnlyList<byte[]> Select(InterceptionTargets targets)
    {
        var selected = new List<byte[]>(2);
        if (targets.HasFlag(InterceptionTargets.Client))
        {
            selected.Add(ToClient);
        }

        if (targets.HasFlag(InterceptionTargets.Gateway))
        {
            selected.Add(ToGateway);
        }

        return selected;
    }
}

public sealed record ArpControllerFrames(
    byte[] ClientToController,
    byte[] GatewayToController);

public static class ArpInterceptionFrames
{
    public static byte[] BuildRecoveryRequest(
        PhysicalAddress controllerMac,
        PhysicalAddress gatewayMac,
        IPAddress gatewayIp,
        PhysicalAddress clientMac,
        IPAddress clientIp) =>
        EthernetFrameCodec.BuildArpRequestWithEthernetSource(
            controllerMac,
            gatewayMac,
            gatewayIp,
            clientMac,
            clientIp);

    public static ArpPeerFrames BuildPoison(
        PhysicalAddress controllerMac,
        IPAddress gatewayIp,
        PhysicalAddress gatewayMac,
        IPAddress clientIp,
        PhysicalAddress clientMac) =>
        new(
            EthernetFrameCodec.BuildArpReply(
                controllerMac,
                gatewayIp,
                clientMac,
                clientIp),
            EthernetFrameCodec.BuildArpReply(
                controllerMac,
                clientIp,
                gatewayMac,
                gatewayIp));

    public static ArpPeerFrames BuildRestore(
        PhysicalAddress controllerMac,
        IPAddress gatewayIp,
        PhysicalAddress gatewayMac,
        IPAddress clientIp,
        PhysicalAddress clientMac) =>
        new(
            EthernetFrameCodec.BuildArpReplyWithEthernetSource(
                controllerMac,
                gatewayMac,
                gatewayIp,
                clientMac,
                clientIp),
            EthernetFrameCodec.BuildArpReply(
                clientMac,
                clientIp,
                gatewayMac,
                gatewayIp));

    public static ArpControllerFrames BuildControllerProtection(
        PhysicalAddress controllerMac,
        IPAddress controllerIp,
        PhysicalAddress gatewayMac,
        IPAddress gatewayIp,
        PhysicalAddress clientMac,
        IPAddress clientIp) =>
        new(
            EthernetFrameCodec.BuildArpReplyWithEthernetSource(
                controllerMac,
                clientMac,
                clientIp,
                controllerMac,
                controllerIp),
            EthernetFrameCodec.BuildArpReplyWithEthernetSource(
                gatewayMac,
                gatewayMac,
                gatewayIp,
                controllerMac,
                controllerIp));
}
