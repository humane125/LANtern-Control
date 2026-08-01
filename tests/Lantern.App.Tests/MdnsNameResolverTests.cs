using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class MdnsNameResolverTests
{
    [Fact]
    public void ParsePtrResponse_ReturnsLocalHostName()
    {
        var packet = Convert.FromHexString(
            "4C4184000000000100000000" +
            "00" +
            "000C0001000000780014" +
            "0C506978656C2D31302D50726F" +
            "056C6F63616C00");

        var name = MdnsNameResolver.ParsePtrResponse(packet);

        Assert.Equal("Pixel-10-Pro", name);
    }

    [Fact]
    public void ParsePtrResponse_RejectsMalformedPacket()
    {
        Assert.Null(MdnsNameResolver.ParsePtrResponse([0x00, 0x01]));
    }
}
