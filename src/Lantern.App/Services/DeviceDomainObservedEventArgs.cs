using System.Net.NetworkInformation;
using Lantern.Core.Networking;

namespace Lantern.App.Services;

public sealed class DeviceDomainObservedEventArgs(
    PhysicalAddress macAddress,
    DomainObservation observation,
    DateTimeOffset observedAt,
    bool blocked = false) : EventArgs
{
    public PhysicalAddress MacAddress { get; } = macAddress;

    public DomainObservation Observation { get; } = observation;

    public DateTimeOffset ObservedAt { get; } = observedAt;

    public bool Blocked { get; } = blocked;
}
