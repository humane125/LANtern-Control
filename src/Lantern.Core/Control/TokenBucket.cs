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
            Refill();

            if (!CanConsume(byteCount))
            {
                return false;
            }

            Consume(byteCount);
            return true;
        }
    }

    public static bool TryConsumeBoth(
        TokenBucket parent,
        TokenBucket child,
        int byteCount)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        lock (parent.sync)
        {
            lock (child.sync)
            {
                parent.Refill();
                child.Refill();
                if (!parent.CanConsume(byteCount) || !child.CanConsume(byteCount))
                {
                    return false;
                }

                parent.Consume(byteCount);
                child.Consume(byteCount);
                return true;
            }
        }
    }

    private void Refill()
    {
        if (bytesPerSecond == 0)
        {
            return;
        }

        var now = clockSeconds();
        var elapsed = Math.Max(0, now - lastRefill);
        available = Math.Min(capacity, available + (elapsed * bytesPerSecond));
        lastRefill = now;
    }

    private bool CanConsume(int byteCount) =>
        bytesPerSecond == 0 || available + 0.000001 >= byteCount;

    private void Consume(int byteCount)
    {
        if (bytesPerSecond > 0)
        {
            available -= byteCount;
        }
    }
}
