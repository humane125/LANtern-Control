using Lantern.App.ViewModels;

namespace Lantern.App.Controls;

public readonly record struct TrafficChartWindow(DateTimeOffset Start, DateTimeOffset End);

public static class TrafficChartScale
{
    public static IReadOnlyList<TrafficSample> GetRenderSamples(
        IReadOnlyList<TrafficSample> samples,
        int maximumPoints)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (maximumPoints < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        }

        // The visible history is only ten minutes (240 2.5-second samples),
        // so preserve every real sample. The former min/max bucket reduction
        // distorted adjacent spikes and made hover points disagree with the
        // actual timeline.
        return samples;
    }

    public static double GetMaximum(IReadOnlyList<TrafficSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var maximum = samples
            .SelectMany(sample => new[]
            {
                sample.DownloadBytesPerSecond,
                sample.UploadBytesPerSecond,
            })
            .DefaultIfEmpty(0)
            .Max();
        return maximum <= 0 ? 1 : maximum * 1.2;
    }

    public static double GetX(
        DateTimeOffset timestamp,
        DateTimeOffset start,
        DateTimeOffset end,
        double width)
    {
        if (width <= 0)
        {
            return 0;
        }

        var duration = (end - start).TotalMilliseconds;
        if (duration <= 0)
        {
            return 0;
        }

        var progress = (timestamp - start).TotalMilliseconds / duration;
        return Math.Clamp(progress, 0, 1) * width;
    }

    public static int GetNearestSampleIndex(
        IReadOnlyList<TrafficSample> samples,
        double pointerX,
        DateTimeOffset start,
        DateTimeOffset end,
        double width)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return -1;
        }

        return Enumerable.Range(0, samples.Count)
            .MinBy(index => Math.Abs(
                GetX(samples[index].Timestamp, start, end, width) - pointerX));
    }

    public static TrafficChartWindow GetWindow(
        IReadOnlyList<TrafficSample> samples,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one traffic sample is required.", nameof(samples));
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var first = samples[0].Timestamp;
        var latest = samples[^1].Timestamp;
        if (latest - first < duration)
        {
            return new TrafficChartWindow(first, latest);
        }

        return new TrafficChartWindow(latest - duration, latest);
    }

}
