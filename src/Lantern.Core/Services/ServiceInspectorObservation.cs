using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Networking;

namespace Lantern.Core.Services;

public readonly record struct ServiceInspectorObservation(
    PhysicalAddress ClientMac,
    TrafficDirection Direction,
    int MeteredByteCount,
    DomainObservation? DomainObservation,
    ServiceFlowKey? Flow,
    string? AttributedDomain)
{
    public static ServiceInspectorObservation FromRouteResult(FrameRouteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ClientMac is null || result.Direction is null)
        {
            throw new ArgumentException(
                "A service observation requires a client and traffic direction.",
                nameof(result));
        }

        return new ServiceInspectorObservation(
            result.ClientMac,
            result.Direction.Value,
            result.MeteredByteCount,
            result.Observation,
            result.Flow,
            result.AttributedDomain);
    }
}
