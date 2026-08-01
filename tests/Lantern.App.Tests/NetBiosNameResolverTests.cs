using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class NetBiosNameResolverTests
{
    [Fact]
    public void ParseNodeStatusResponse_ReturnsWorkstationName()
    {
        var packet = Convert.FromHexString(
            "123485000000000100000000" +
            "C00C00210001000000000013" +
            "01" +
            "4841595448454D2D50432020202020" +
            "00" +
            "0000");

        var name = NetBiosNameResolver.ParseNodeStatusResponse(packet);

        Assert.Equal("HAYTHEM-PC", name);
    }

    [Fact]
    public void ParseNodeStatusResponse_IgnoresGroupNames()
    {
        var packet = Convert.FromHexString(
            "123485000000000100000000" +
            "C00C00210001000000000013" +
            "01" +
            "574F524B47524F5550202020202020" +
            "00" +
            "8000");

        var name = NetBiosNameResolver.ParseNodeStatusResponse(packet);

        Assert.Null(name);
    }
}
