using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DiscoveryRefreshResultTests
{
    [Fact]
    public void StatusMessage_ReportsRawArpSweepResults()
    {
        var result = new DiscoveryRefreshResult(252, 3, 4);

        Assert.Equal(
            "ARP sweep complete — 252 probes, 3 replies, 4 known devices.",
            result.StatusMessage);
    }

    [Fact]
    public void StatusMessage_UsesSingularReply()
    {
        var result = new DiscoveryRefreshResult(252, 1, 2);

        Assert.Contains("1 reply,", result.StatusMessage, StringComparison.Ordinal);
    }
}
