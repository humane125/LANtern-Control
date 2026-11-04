using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class MacOffloadManagerTests
{
    [Fact]
    public void IsLroEnabled_ReturnsTrueWhenOptionsListContainsLro()
    {
        const string output =
            "\toptions=6467<RXCSUM,TXCSUM,VLAN_MTU,TSO4,TSO6,LRO,CHANNEL_IO>";

        Assert.True(MacOffloadManager.IsLroEnabled(output));
    }

    [Theory]
    [InlineData("\toptions=6460<TSO4,TSO6,CHANNEL_IO,PARTIAL_CSUM>")]
    [InlineData("\toptions=400<LROX,CHANNEL_IO>")]
    [InlineData("\toptions=400<CHANNEL_IO>\n\tother: LRO enabled elsewhere")]
    public void IsLroEnabled_ReturnsFalseWithoutAnExactLroOption(string output) =>
        Assert.False(MacOffloadManager.IsLroEnabled(output));
}
