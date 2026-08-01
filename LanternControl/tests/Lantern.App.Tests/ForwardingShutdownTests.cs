using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class ForwardingShutdownTests
{
    [Fact]
    public async Task RunAsync_RestoresPeersBeforeForwardingStops()
    {
        var forwarding = true;
        var restoredWhileForwarding = false;

        await ForwardingShutdown.RunAsync(
            async () =>
            {
                restoredWhileForwarding = forwarding;
                await Task.Yield();
            },
            () => forwarding = false,
            () => Task.CompletedTask);

        Assert.True(restoredWhileForwarding);
        Assert.False(forwarding);
    }

    [Fact]
    public async Task RunAsync_StillStopsForwardingWhenRestorationFails()
    {
        var forwarding = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ForwardingShutdown.RunAsync(
                () => throw new InvalidOperationException("restore failed"),
                () => forwarding = false,
                () => Task.CompletedTask));

        Assert.False(forwarding);
    }
}
