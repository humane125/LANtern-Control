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
    byte[]? Frame = null,
    int MeteredByteCount = 0,
    DomainObservation? Observation = null,
    bool BlockedByDomain = false);

public sealed class FrameRouter
{
    private const int MaxObservedFlows = 8192;
    private readonly PhysicalAddress localMac;
    private readonly IPAddress localIp;
    private readonly PhysicalAddress gatewayMac;
    private readonly TrafficPolicy policy;
    private readonly ConcurrentDictionary<IPAddress, PhysicalAddress> clients;
    private readonly ConcurrentDictionary<ObservedFlowKey, string> observedFlowDomains = [];

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

        TransportFlow? transportFlow =
            NetworkActivityParser.TryParseTransportFlow(frame, out var parsedFlow)
                ? parsedFlow
                : null;

        if (ipv4.Source.Equals(localIp) || ipv4.Destination.Equals(localIp))
        {
            return new FrameRouteResult(FrameAction.Ignore);
        }

        if (ipv4.DestinationMac.Equals(localMac) &&
            clients.TryGetValue(ipv4.Source, out var uploadClientMac) &&
            ipv4.SourceMac.Equals(uploadClientMac))
        {
            DomainObservation? observation =
                NetworkActivityParser.TryParseOutbound(frame, out var parsed)
                    ? parsed
                    : null;
            return RouteForClient(
                frame,
                uploadClientMac,
                TrafficDirection.Upload,
                gatewayMac,
                ipv4.TransportPayloadLength,
                observation,
                transportFlow);
        }

        if (ipv4.DestinationMac.Equals(localMac) &&
            ipv4.SourceMac.Equals(gatewayMac) &&
            clients.TryGetValue(ipv4.Destination, out var downloadClientMac))
        {
            return RouteForClient(
                frame,
                downloadClientMac,
                TrafficDirection.Download,
                downloadClientMac,
                ipv4.TransportPayloadLength,
                null,
                transportFlow);
        }

        return new FrameRouteResult(FrameAction.Ignore);
    }

    private FrameRouteResult RouteForClient(
        ReadOnlySpan<byte> frame,
        PhysicalAddress clientMac,
        TrafficDirection direction,
        PhysicalAddress destinationMac,
        int meteredByteCount,
        DomainObservation? observation,
        TransportFlow? transportFlow)
    {
        ObservedFlowKey? flowKey = transportFlow is { } flow
            ? CreateFlowKey(clientMac, direction, flow)
            : null;
        if (direction == TrafficDirection.Upload &&
            observation is { Source: not DomainObservationSource.Dns } observedDomain &&
            flowKey is { } observedKey)
        {
            if (observedFlowDomains.Count >= MaxObservedFlows)
            {
                observedFlowDomains.Clear();
            }

            observedFlowDomains[observedKey] = observedDomain.Domain;
        }

        if (flowKey is { } knownKey &&
            observedFlowDomains.TryGetValue(knownKey, out var knownDomain) &&
            policy.ShouldBlockDomain(clientMac.ToString(), knownDomain))
        {
            return new FrameRouteResult(
                FrameAction.Drop,
                direction,
                clientMac,
                Observation: observation,
                BlockedByDomain: true);
        }

        if (direction == TrafficDirection.Upload &&
            policy.GetBlockedDomains(clientMac.ToString()).Count > 0 &&
            NetworkActivityParser.IsOutboundUdpToPort(frame, 443))
        {
            return new FrameRouteResult(
                FrameAction.Drop,
                direction,
                clientMac,
                BlockedByDomain: true);
        }

        if (direction == TrafficDirection.Upload &&
            observation is { } outbound &&
            policy.ShouldBlockDomain(clientMac.ToString(), outbound.Domain))
        {
            return new FrameRouteResult(
                FrameAction.Drop,
                direction,
                clientMac,
                Observation: outbound,
                BlockedByDomain: true);
        }

        if (!policy.ShouldForward(clientMac.ToString(), direction, frame.Length))
        {
            return new FrameRouteResult(
                FrameAction.Drop,
                direction,
                clientMac,
                Observation: observation);
        }

        return new FrameRouteResult(
            FrameAction.Forward,
            direction,
            clientMac,
            EthernetFrameCodec.RewriteEthernetAddresses(
                frame,
                localMac,
                destinationMac),
            meteredByteCount,
            observation);
    }

    private static ObservedFlowKey CreateFlowKey(
        PhysicalAddress clientMac,
        TrafficDirection direction,
        TransportFlow flow) =>
        direction == TrafficDirection.Upload
            ? new ObservedFlowKey(
                TrafficPolicy.NormalizeMac(clientMac.ToString()),
                flow.SourcePort,
                flow.DestinationAddress,
                flow.DestinationPort,
                flow.Protocol)
            : new ObservedFlowKey(
                TrafficPolicy.NormalizeMac(clientMac.ToString()),
                flow.DestinationPort,
                flow.SourceAddress,
                flow.SourcePort,
                flow.Protocol);

    private readonly record struct ObservedFlowKey(
        string ClientMac,
        ushort ClientPort,
        IPAddress RemoteAddress,
        ushort RemotePort,
        byte Protocol);
}
