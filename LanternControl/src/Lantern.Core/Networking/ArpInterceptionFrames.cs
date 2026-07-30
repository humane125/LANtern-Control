using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.Core.Networking;

public sealed record ArpPeerFrames(byte[] ToClient, byte[] ToGateway);

public static class ArpInterceptionFrames
{
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
        PhysicalAddress gatewayMac,
        IPAddress gatewayIp,
        PhysicalAddress clientMac,
        IPAddress clientIp) =>
        new(
            EthernetFrameCodec.BuildArpReply(
                gatewayMac,
                gatewayIp,
                clientMac,
                clientIp),
            EthernetFrameCodec.BuildArpReply(
                clientMac,
                clientIp,
                gatewayMac,
                gatewayIp));
}
