using Lantern.App.Services;
using SharpPcap;
using Xunit;

namespace Lantern.App.Tests;

public sealed class ForwarderStartupPolicyTests
{
    [Fact]
    public void ForwardedPackets_UseTheEstablishedCaptureHandle()
    {
        var establishedCaptureHandle = new object();
        var secondaryForwardingHandle = new object();

        var selected = PacketInjectionPolicy.SelectHandle(
            establishedCaptureHandle,
            secondaryForwardingHandle);

        Assert.Same(establishedCaptureHandle, selected);
    }

    [Fact]
    public async Task FirstCaptureError_FaultsStartupWithTheDriverError()
    {
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ForwarderStartupPolicy.ObserveFirstRead(
                ready,
                GetPacketStatus.Error,
                "The device stopped responding"));

        Assert.Equal(
            "Packet forwarding stopped: The device stopped responding",
            exception.Message);
        var startupException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ready.Task);
        Assert.Equal(exception.Message, startupException.Message);
    }

    [Theory]
    [InlineData(GetPacketStatus.ReadTimeout)]
    [InlineData(GetPacketStatus.PacketRead)]
    public async Task FirstSuccessfulRead_MarksForwarderReady(GetPacketStatus status)
    {
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ForwarderStartupPolicy.ObserveFirstRead(ready, status, null);

        await ready.Task;
    }
}
