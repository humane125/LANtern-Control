using System.Diagnostics;

namespace Lantern.Linux.Services;

public sealed class LinuxFrameDeduplicator
{
    // Eight milliseconds is long enough to catch the immediate duplicate copy
    // produced by a local capture/bridge path, but shorter than this LAN's real
    // TCP retransmission feedback loop. Legitimate later retransmissions pass.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(8);
    private readonly Dictionary<FrameSignature, long> recent = [];
    private readonly Func<long> timestamp;
    private readonly long windowTicks;
    private int framesUntilCleanup = 1024;

    public LinuxFrameDeduplicator(
        Func<long>? timestamp = null,
        long timestampFrequency = 0)
    {
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        var frequency = timestampFrequency > 0
            ? timestampFrequency
            : Stopwatch.Frequency;
        windowTicks = Math.Max(
            1,
            (long)(DuplicateWindow.TotalSeconds * frequency));
    }

    public bool IsDuplicate(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty)
        {
            return false;
        }

        var now = timestamp();
        var signature = new FrameSignature(Hash(frame), frame.Length);
        if (recent.TryGetValue(signature, out var firstSeen) &&
            now - firstSeen >= 0 &&
            now - firstSeen <= windowTicks)
        {
            return true;
        }

        recent[signature] = now;
        if (--framesUntilCleanup <= 0 || recent.Count > 4096)
        {
            Cleanup(now);
        }

        return false;
    }

    public void Clear()
    {
        recent.Clear();
        framesUntilCleanup = 1024;
    }

    private void Cleanup(long now)
    {
        foreach (var pair in recent
                     .Where(pair => now - pair.Value > windowTicks)
                     .ToArray())
        {
            recent.Remove(pair.Key);
        }

        framesUntilCleanup = 1024;
    }

    private static ulong Hash(ReadOnlySpan<byte> frame)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        foreach (var value in frame)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    private readonly record struct FrameSignature(ulong Hash, int Length);
}
