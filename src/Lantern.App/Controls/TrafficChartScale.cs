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

        if (samples.Count <= maximumPoints)
        {
            return samples;
        }

        if (maximumPoints == 2)
        {
            return [samples[0], samples[^1]];
        }

        if (maximumPoints == 3)
        {
            var peak = samples
                .Skip(1)
                .Take(samples.Count - 2)
                .MaxBy(GetCombinedRate)!;
            return [samples[0], peak, samples[^1]];
        }

        var result = new List<TrafficSample>(maximumPoints) { samples[0] };
        var interiorCount = samples.Count - 2;
        var bucketCount = Math.Max(1, (maximumPoints - 2) / 2);
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = 1 + (int)((long)bucket * interiorCount / bucketCount);
            var end = 1 + (int)((long)(bucket + 1) * interiorCount / bucketCount);
            if (start >= end)
            {
                continue;
            }

            var minimumIndex = start;
            var maximumIndex = start;
            for (var index = start + 1; index < end; index++)
            {
                if (GetCombinedRate(samples[index]) < GetCombinedRate(samples[minimumIndex]))
                {
                    minimumIndex = index;
                }

                if (GetCombinedRate(samples[index]) > GetCombinedRate(samples[maximumIndex]))
                {
                    maximumIndex = index;
                }
            }

            if (minimumIndex == maximumIndex)
            {
                result.Add(samples[minimumIndex]);
            }
            else if (minimumIndex < maximumIndex)
            {
                result.Add(samples[minimumIndex]);
                result.Add(samples[maximumIndex]);
            }
            else
            {
                result.Add(samples[maximumIndex]);
                result.Add(samples[minimumIndex]);
            }
        }

        result.Add(samples[^1]);
        return result;
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

    private static double GetCombinedRate(TrafficSample sample) =>
        sample.DownloadBytesPerSecond + sample.UploadBytesPerSecond;
}
