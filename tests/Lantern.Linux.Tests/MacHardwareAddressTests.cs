using System.Net.NetworkInformation;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class MacHardwareAddressTests
{
    [Theory]
    [InlineData("1:2:3:4:5")]
    [InlineData("1a:2b:3c:4d:5e:6f:70")]
    [InlineData("1a:2b:3c:4d:5e:6fa")]
    [InlineData("1a:2b::4d:5e:6f")]
    public void TryParse_RejectsMalformedAddresses(string text) =>
        Assert.False(MacHardwareAddress.TryParse(text, out _));

    [Theory]
    [InlineData("020000000000", true)]
    [InlineData("000000000000", true)]
    [InlineData("1A2B3C4D5E6F", false)]
    public void IsUnusable_FlagsPlaceholderAndZeroAddresses(string mac, bool expected) =>
        Assert.Equal(expected, MacHardwareAddress.IsUnusable(PhysicalAddress.Parse(mac)));
}
