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
    bool BlockedByDomain = false,
    ServiceFlowKey? Flow = null,
    string? AttributedDomain = null);

public readonly record struct ServiceFlowKey(
    string ClientMac,
    ushort ClientPort,
    IPAddress RemoteAddress,
    ushort RemotePort,
    byte Protocol);

public sealed class FrameRouter
{
    private const int MaxObservedFlows = 8192;
    private readonly PhysicalAddress localMac;
    private readonly IPAddress localIp;
    private readonly PhysicalAddress gatewayMac;
    private readonly TrafficPolicy policy;
    private readonly bool enforceRateLimits;
    private readonly ConcurrentDictionary<IPAddress, PhysicalAddress> clients;
    private readonly ConcurrentDictionary<ServiceFlowKey, string> observedFlowDomains = [];

    public FrameRouter(
        PhysicalAddress localMac,
        IPAddress localIp,
        PhysicalAddress gatewayMac,
        IReadOnlyDictionary<IPAddress, PhysicalAddress> clients,
        TrafficPolicy policy,
        bool enforceRateLimits = true)
    {
        this.localMac = localMac;
        this.localIp = localIp;
        this.gatewayMac = gatewayMac;
        this.policy = policy;
        this.enforceRateLimits = enforceRateLimits;
        this.clients = new ConcurrentDictionary<IPAddress, PhysicalAddress>(clients);
    }

    public void UpdateClient(IPAddress address, PhysicalAddress macAddress)
    {
        if (!address.Equals(localIp))
        {
            foreach (var staleAddress in clients
                         .Where(pair =>
                             pair.Value.Equals(macAddress) &&
                             !pair.Key.Equals(address))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                clients.TryRemove(staleAddress, out _);
            }

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
        ServiceFlowKey? flowKey = transportFlow is { } flow
            ? CreateFlowKey(clientMac, direction, flow)
            : null;
        string? attributedDomain = null;
        if (direction == TrafficDirection.Upload &&
            observation is { Source: not DomainObservationSource.Dns } observedDomain &&
            flowKey is { } observedKey)
        {
            if (observedFlowDomains.Count >= MaxObservedFlows)
            {
                observedFlowDomains.Clear();
            }

            observedFlowDomains[observedKey] = observedDomain.Domain;
            attributedDomain = observedDomain.Domain;
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
                BlockedByDomain: true,
                Flow: flowKey,
                AttributedDomain: knownDomain);
        }

        if (flowKey is { } attributedKey &&
            observedFlowDomains.TryGetValue(attributedKey, out var rememberedDomain))
        {
            attributedDomain = rememberedDomain;
        }

        if (direction == TrafficDirection.Upload &&
            policy.GetBlockedDomains(clientMac.ToString()).Count > 0 &&
            NetworkActivityParser.IsOutboundUdpToPort(frame, 443))
        {
            return new FrameRouteResult(
                FrameAction.Drop,
                direction,
                clientMac,
                BlockedByDomain: true,
                Flow: flowKey);
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
                BlockedByDomain: true,
                Flow: flowKey,
                AttributedDomain: outbound.Source == DomainObservationSource.Dns
                    ? null
                    : outbound.Domain);
        }

        var shouldForward = enforceRateLimits
            ? policy.ShouldForward(clientMac.ToString(), direction, frame.Length)
            : !policy.GetRule(clientMac.ToString()).PauseInternet;
        if (!shouldForward)
        {
            return new FrameRouteResult(
                FrameAction.Drop,
                direction,
                clientMac,
                Observation: observation,
                Flow: observation is { Source: DomainObservationSource.Dns } ? null : flowKey,
                AttributedDomain: attributedDomain);
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
            observation,
            Flow: observation is { Source: DomainObservationSource.Dns } ? null : flowKey,
            AttributedDomain: attributedDomain);
    }

    private static ServiceFlowKey CreateFlowKey(
        PhysicalAddress clientMac,
        TrafficDirection direction,
        TransportFlow flow) =>
        direction == TrafficDirection.Upload
            ? new ServiceFlowKey(
                TrafficPolicy.NormalizeMac(clientMac.ToString()),
                flow.SourcePort,
                flow.DestinationAddress,
                flow.DestinationPort,
                flow.Protocol)
            : new ServiceFlowKey(
                TrafficPolicy.NormalizeMac(clientMac.ToString()),
                flow.DestinationPort,
                flow.SourceAddress,
                flow.SourcePort,
                flow.Protocol);
}
