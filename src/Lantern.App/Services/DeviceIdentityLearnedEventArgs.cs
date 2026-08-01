using System.Net.NetworkInformation;

namespace Lantern.App.Services;

public sealed class DeviceIdentityLearnedEventArgs(
    PhysicalAddress macAddress,
    string hostName) : EventArgs
{
    public PhysicalAddress MacAddress { get; } = macAddress;

    public string HostName { get; } = hostName;
}
