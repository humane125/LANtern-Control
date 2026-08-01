using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;

namespace Lantern.Core.Networking;

public enum FrameAction
{
    Ignore,
    Drop,
    Forward,
}

public sealed record FrameRouteResult(
    FrameAction Action,
    TrafficDirection? Direction = null,
    PhysicalAddress? ClientMac = null,
    byte[]? Frame = null);

public sealed class FrameRouter
{
    private readonly PhysicalAddress localMac;
    private readonly IPAddress localIp;
    private readonly PhysicalAddress gatewayMac;
    private readonly TrafficPolicy policy;
    private readonly ConcurrentDictionary<IPAddress, PhysicalAddress> clients;

    public FrameRouter(
        PhysicalAddress localMac,
        IPAddress localIp,
        PhysicalAddress gatewayMac,
        IReadOnlyDictionary<IPAddress, PhysicalAddress> clients,
        TrafficPolicy policy)
    {
        this.localMac = localMac;
        this.localIp = localIp;
        this.gatewayMac = gatewayMac;
        this.policy = policy;
        this.clients = new ConcurrentDictionary<IPAddress, PhysicalAddress>(clients);
    }

    public void UpdateClient(IPAddress address, PhysicalAddress macAddress)
    {
        if (!address.Equals(localIp))
        {
            clients[address] = macAddress;
        }
    }

    public void RemoveClient(IPAddress address) => clients.TryRemove(address, out _);

    public FrameRouteResult Route(ReadOnlySpan<byte> frame)
    {
        if (!EthernetFrameCodec.TryParseIpv4(frame, out var ipv4))
        {
            return new FrameRouteResult(FrameAction.Ignore);
        }

        if (ipv4.Source.Equals(localIp) || ipv4.Destination.Equals(localIp))
        {
            return new FrameRouteResult(FrameAction.Ignore);
        }

        if (ipv4.DestinationMac.Equals(localMac) &&
            clients.TryGetValue(ipv4.Source, out var uploadClientMac) &&
            ipv4.SourceMac.Equals(uploadClientMac))
        {
            return RouteForClient(
                frame,
                uploadClientMac,
                TrafficDirection.Upload,
                gatewayMac);
        }

        if (ipv4.DestinationMac.Equals(localMac) &&
            ipv4.SourceMac.Equals(gatewayMac) &&
            clients.TryGetValue(ipv4.Destination, out var downloadClientMac))
        {
            return RouteForClient(
                frame,
                downloadClientMac,
                TrafficDirection.Download,
                downloadClientMac);
        }

        return new FrameRouteResult(FrameAction.Ignore);
    }

    private FrameRouteResult RouteForClient(
        ReadOnlySpan<byte> frame,
        PhysicalAddress clientMac,
        TrafficDirection direction,
        PhysicalAddress destinationMac)
    {
        if (!policy.ShouldForward(clientMac.ToString(), direction, frame.Length))
        {
            return new FrameRouteResult(FrameAction.Drop, direction, clientMac);
        }

        return new FrameRouteResult(
            FrameAction.Forward,
            direction,
            clientMac,
            EthernetFrameCodec.RewriteEthernetAddresses(
                frame,
                localMac,
                destinationMac));
    }
}
