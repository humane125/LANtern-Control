using System.Net.NetworkInformation;
using Lantern.App.Services;
using Lantern.Core.Networking;
using Xunit;

namespace Lantern.App.Tests;

public sealed class WindowsAdapterServiceTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, AdapterConnectionKind.Wifi)]
    [InlineData(NetworkInterfaceType.Ethernet, AdapterConnectionKind.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, AdapterConnectionKind.Ethernet)]
    [InlineData(NetworkInterfaceType.Unknown, AdapterConnectionKind.Unknown)]
    public void MapConnectionKind_UsesNetworkInterfaceType(
        NetworkInterfaceType interfaceType,
        AdapterConnectionKind expected)
    {
        Assert.Equal(expected, WindowsAdapterService.MapConnectionKind(interfaceType));
    }
}
