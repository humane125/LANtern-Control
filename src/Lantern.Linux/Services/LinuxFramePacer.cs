using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Channels;
using Lantern.Core.Control;

namespace Lantern.Linux.Services;

public sealed class LinuxFramePacer : IAsyncDisposable
{
    private const int MinimumLimitedQueueBytes = 10 * 1_514;
    private const int UnlimitedQueueBytes = 4 * 1024 * 1024;
    private readonly TrafficPolicy policy;
    private readonly Action<byte[]> sendFrame;
    private readonly Func<double> clockSeconds;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Action frameDropped;
    private readonly Action<Exception> failed;
    private readonly int queueCapacity;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentDictionary<QueueKey, Lazy<FrameQueue>> queues = [];
    private int disposed;

    public LinuxFramePacer(
        TrafficPolicy policy,
        Action<byte[]> sendFrame,
        Func<double>? clockSeconds = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Action? frameDropped = null,
        Action<Exception>? failed = null,
        int queueCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(sendFrame);
        if (queueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }

        this.policy = policy;
        this.sendFrame = sendFrame;
        this.clockSeconds = clockSeconds ?? ReadStopwatch;
        this.delayAsync = delayAsync ?? Task.Delay;
        this.frameDropped = frameDropped ?? (() => { });
        this.failed = failed ?? (_ => { });
        this.queueCapacity = queueCapacity;
    }

    public bool TryEnqueue(
        PhysicalAddress clientMac,
        TrafficDirection direction,
        byte[] frame)
    {
        return TryEnqueue(clientMac, null, direction, frame);
    }

    public bool TryEnqueue(
        PhysicalAddress clientMac,
        string? serviceId,
        TrafficDirection direction,
        byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(clientMac);
        ArgumentNullException.ThrowIfNull(frame);
        if (Volatile.Read(ref disposed) != 0)
        {
            return false;
        }

        var key = new QueueKey(
            TrafficPolicy.NormalizeMac(clientMac.ToString()),
            direction);
        var queue = queues.GetOrAdd(
            key,
            value => new Lazy<FrameQueue>(
                () => new FrameQueue(this, value),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        var rule = policy.GetRule(key.ClientMac);
        var serviceRule = policy.GetServiceRuleForTraffic(key.ClientMac, serviceId);
        var deviceRate = key.Direction == TrafficDirection.Download
            ? rule.DownloadKiloBytesPerSecond
            : rule.UploadKiloBytesPerSecond;
        var serviceRate = key.Direction == TrafficDirection.Download
            ? serviceRule.DownloadKiloBytesPerSecond
            : serviceRule.UploadKiloBytesPerSecond;
        var limitedRates = new[] { deviceRate, serviceRate }.Where(rate => rate > 0).ToArray();
        var effectiveRate = limitedRates.Length == 0 ? 0 : limitedRates.Min();
        var maximumQueuedBytes = effectiveRate <= 0
            ? (long)UnlimitedQueueBytes
            : Math.Max((long)MinimumLimitedQueueBytes, effectiveRate * 250L);
        return queue.TryWrite(new QueuedFrame(serviceId, frame), maximumQueuedBytes);
    }

    public async Task ResetAsync(PhysicalAddress clientMac)
    {
        ArgumentNullException.ThrowIfNull(clientMac);
        var normalized = TrafficPolicy.NormalizeMac(clientMac.ToString());
        var removed = new List<FrameQueue>();
        foreach (var direction in Enum.GetValues<TrafficDirection>())
        {
            if (queues.TryRemove(new QueueKey(normalized, direction), out var lazyQueue) &&
                lazyQueue.IsValueCreated)
            {
                removed.Add(lazyQueue.Value);
            }
        }

        await Task.WhenAll(removed.Select(queue => queue.StopAsync()));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        cancellation.Cancel();
        var createdQueues = queues.Values
            .Where(queue => queue.IsValueCreated)
            .Select(queue => queue.Value)
            .ToArray();
        await Task.WhenAll(createdQueues.Select(queue => queue.StopAsync()));

        cancellation.Dispose();
    }

    private async Task RunQueueAsync(
        QueueKey key,
        ChannelReader<QueuedFrame> reader,
        FrameQueue queue,
        CancellationToken cancellationToken)
    {
        var nextSendAt = clockSeconds();
        var previousRate = -1;
        var serviceSchedules = new Dictionary<string, ServiceSchedule>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            await foreach (var queued in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var rule = policy.GetRule(key.ClientMac);
                    if (rule.PauseInternet)
                    {
                        frameDropped();
                        continue;
                    }

                    var serviceRule = policy.GetServiceRuleForTraffic(
                        key.ClientMac,
                        queued.ServiceId);
                    var deviceRate = key.Direction == TrafficDirection.Download
                        ? rule.DownloadKiloBytesPerSecond
                        : rule.UploadKiloBytesPerSecond;
                    var serviceRate = key.Direction == TrafficDirection.Download
                        ? serviceRule.DownloadKiloBytesPerSecond
                        : serviceRule.UploadKiloBytesPerSecond;
                    var now = clockSeconds();
                    if (deviceRate <= 0)
                    {
                        previousRate = 0;
                        nextSendAt = now;
                    }
                    else
                    {
                        if (deviceRate != previousRate)
                        {
                            previousRate = deviceRate;
                            nextSendAt = now;
                        }
                    }

                    ServiceSchedule? serviceSchedule = null;
                    if (serviceRate > 0 && !string.IsNullOrWhiteSpace(queued.ServiceId))
                    {
                        if (!serviceSchedules.TryGetValue(
                                queued.ServiceId,
                                out serviceSchedule) ||
                            serviceSchedule.Rate != serviceRate)
                        {
                            serviceSchedule = new ServiceSchedule(serviceRate, now);
                            serviceSchedules[queued.ServiceId] = serviceSchedule;
                        }
                    }

                    var scheduledAt = Math.Max(
                        deviceRate > 0 ? nextSendAt : now,
                        serviceSchedule?.NextSendAt ?? now);
                    if (scheduledAt > now)
                    {
                        await delayAsync(
                            TimeSpan.FromSeconds(scheduledAt - now),
                            cancellationToken);
                    }

                    rule = policy.GetRule(key.ClientMac);
                    if (rule.PauseInternet)
                    {
                        frameDropped();
                        continue;
                    }

                    sendFrame(queued.Frame);
                    var scheduleBase = Math.Max(scheduledAt, clockSeconds() - 0.05D);
                    if (deviceRate > 0)
                    {
                        nextSendAt = scheduleBase +
                            (queued.Frame.Length / (deviceRate * 1_000D));
                    }

                    if (serviceSchedule is not null)
                    {
                        serviceSchedule.NextSendAt = scheduleBase +
                            (queued.Frame.Length / (serviceRate * 1_000D));
                    }
                }
                finally
                {
                    queue.Release(queued.Frame.Length);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failed(exception);
        }
    }

    private static double ReadStopwatch() =>
        Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private readonly record struct QueueKey(
        string ClientMac,
        TrafficDirection Direction);

    private readonly record struct QueuedFrame(string? ServiceId, byte[] Frame);

    private sealed class ServiceSchedule(int rate, double nextSendAt)
    {
        public int Rate { get; } = rate;

        public double NextSendAt { get; set; } = nextSendAt;
    }

    private sealed class FrameQueue
    {
        private long queuedBytes;
        private readonly CancellationTokenSource cancellation;
        private int stopped;

        public FrameQueue(LinuxFramePacer owner, QueueKey key)
        {
            var channel = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(
                owner.queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                owner.cancellation.Token);
            Writer = channel.Writer;
            Worker = Task.Run(() => owner.RunQueueAsync(
                key,
                channel.Reader,
                this,
                cancellation.Token));
        }

        public ChannelWriter<QueuedFrame> Writer { get; }
        public Task Worker { get; }

        public bool TryWrite(QueuedFrame frame, long maximumQueuedBytes)
        {
            var total = Interlocked.Add(ref queuedBytes, frame.Frame.Length);
            if (total > maximumQueuedBytes || !Writer.TryWrite(frame))
            {
                Interlocked.Add(ref queuedBytes, -frame.Frame.Length);
                return false;
            }

            return true;
        }

        public void Release(int byteCount) =>
            Interlocked.Add(ref queuedBytes, -byteCount);

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
            {
                await Worker;
                return;
            }

            cancellation.Cancel();
            Writer.TryComplete();
            try
            {
                await Worker;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
