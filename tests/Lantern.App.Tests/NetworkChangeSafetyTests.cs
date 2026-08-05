using System.Net;
using System.Net.NetworkInformation;
using Lantern.App.Services;
using Lantern.Core.Networking;
using Xunit;

namespace Lantern.App.Tests;

public sealed class NetworkChangeSafetyTests
{
    private static readonly AdapterProfile ActiveProfile = new(
        "wifi-adapter",
        "Wi-Fi",
        "Wireless adapter",
        IPAddress.Parse("192.168.100.20"),
        24,
        IPAddress.Parse("192.168.100.1"),
        PhysicalAddress.Parse("001122334455"));

    [Fact]
    public void AdapterSession_RemainsValidOnTheSameNetwork()
    {
        var refreshed = ActiveProfile with { Name = "Wi-Fi 2" };

        Assert.False(AdapterNetworkChangeDetector.HasChanged(
            ActiveProfile,
            [refreshed]));
    }

    [Fact]
    public void AdapterSession_ChangesWhenWifiMovesToAnotherGateway()
    {
        var refreshed = ActiveProfile with
        {
            LocalAddress = IPAddress.Parse("192.168.31.20"),
            GatewayAddress = IPAddress.Parse("192.168.31.1"),
        };

        Assert.True(AdapterNetworkChangeDetector.HasChanged(
            ActiveProfile,
            [refreshed]));
    }

    [Fact]
    public void AdapterSession_ChangesWhenTheActiveAdapterDisappears()
    {
        Assert.True(AdapterNetworkChangeDetector.HasChanged(ActiveProfile, []));
    }

    [Fact]
    public void ExceptionDialogGate_AllowsOnlyTheFirstUnhandledError()
    {
        var gate = new ExceptionDialogGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }
}
