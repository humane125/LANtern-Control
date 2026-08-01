using System.Diagnostics;

namespace Lantern.Core.Control;

public sealed class TokenBucket
{
    private readonly object sync = new();
    private readonly double bytesPerSecond;
    private readonly double capacity;
    private readonly Func<double> clockSeconds;
    private double available;
    private double lastRefill;

    public TokenBucket(
        double bytesPerSecond,
        Func<double>? clockSeconds = null,
        double burstSeconds = 1.5)
    {
        if (bytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        }

        if (burstSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(burstSeconds));
        }

        this.bytesPerSecond = bytesPerSecond;
        this.clockSeconds = clockSeconds ?? ReadStopwatch;
        capacity = bytesPerSecond * burstSeconds;
        available = capacity;
        lastRefill = this.clockSeconds();
    }

    private static double ReadStopwatch() =>
        Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public bool TryConsume(int byteCount)
    {
        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        if (bytesPerSecond == 0)
        {
            return true;
        }

        lock (sync)
        {
            var now = clockSeconds();
            var elapsed = Math.Max(0, now - lastRefill);
            available = Math.Min(capacity, available + (elapsed * bytesPerSecond));
            lastRefill = now;

            if (available + 0.000001 < byteCount)
            {
                return false;
            }

            available -= byteCount;
            return true;
        }
    }
}
