namespace Lantern.App.ViewModels;

public sealed class TrafficHistory
{
    private readonly int capacity;
    private readonly TimeSpan? retentionPeriod;
    private readonly List<TrafficSample> samples = [];

    public TrafficHistory(int capacity, TimeSpan? retentionPeriod = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (retentionPeriod is { } retention && retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionPeriod));
        }

        this.capacity = capacity;
        this.retentionPeriod = retentionPeriod;
    }

    public IReadOnlyList<TrafficSample> Samples => samples.ToArray();

    public void Add(TrafficSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        samples.Add(sample);
        if (retentionPeriod is { } retention)
        {
            var cutoff = sample.Timestamp - retention;
            samples.RemoveAll(existing => existing.Timestamp < cutoff);
        }

        if (samples.Count > capacity)
        {
            samples.RemoveRange(0, samples.Count - capacity);
        }
    }

    public bool TryAdd(TrafficSample sample, TimeSpan minimumInterval)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        }

        if (samples.Count > 0 && sample.Timestamp - samples[^1].Timestamp < minimumInterval)
        {
            return false;
        }

        Add(sample);
        return true;
    }

    public void Clear() => samples.Clear();
}
