using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.App.Services;

public sealed record KnownDeviceHint(
    PhysicalAddress MacAddress,
    IPAddress? LastKnownIp);
