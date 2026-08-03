using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxFrameDeduplicatorTests
{
    [Fact]
    public void ImmediateIdenticalCapture_IsRecognizedAsDuplicate()
    {
        var clock = new ManualClock();
        var deduplicator = new LinuxFrameDeduplicator(
            clock.Read,
            TimeSpan.TicksPerSecond);
        byte[] frame = [1, 2, 3, 4, 5];

        Assert.False(deduplicator.IsDuplicate(frame));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(deduplicator.IsDuplicate(frame));
    }

    [Fact]
    public void IdenticalFrameAfterWindow_IsForwardedAsRealRetransmission()
    {
        var clock = new ManualClock();
        var deduplicator = new LinuxFrameDeduplicator(
            clock.Read,
            TimeSpan.TicksPerSecond);
        byte[] frame = [1, 2, 3, 4, 5];

        Assert.False(deduplicator.IsDuplicate(frame));
        clock.Advance(TimeSpan.FromMilliseconds(10));

        Assert.False(deduplicator.IsDuplicate(frame));
    }

    [Fact]
    public void DifferentFrameInsideWindow_IsNotSuppressed()
    {
        var clock = new ManualClock();
        var deduplicator = new LinuxFrameDeduplicator(
            clock.Read,
            TimeSpan.TicksPerSecond);

        Assert.False(deduplicator.IsDuplicate([1, 2, 3, 4, 5]));
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.False(deduplicator.IsDuplicate([1, 2, 3, 4, 6]));
    }

    private sealed class ManualClock
    {
        private long timestamp;

        public long Read() => timestamp;

        public void Advance(TimeSpan duration) =>
            timestamp += (long)(duration.TotalSeconds * TimeSpan.TicksPerSecond);
    }
}
