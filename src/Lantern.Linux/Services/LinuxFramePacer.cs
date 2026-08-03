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
        var kiloBytesPerSecond = key.Direction == TrafficDirection.Download
            ? rule.DownloadKiloBytesPerSecond
            : rule.UploadKiloBytesPerSecond;
        var maximumQueuedBytes = kiloBytesPerSecond <= 0
            ? (long)UnlimitedQueueBytes
            : Math.Max((long)MinimumLimitedQueueBytes, kiloBytesPerSecond * 250L);
        return queue.TryWrite(frame, maximumQueuedBytes);
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
        ChannelReader<byte[]> reader,
        FrameQueue queue,
        CancellationToken cancellationToken)
    {
        var nextSendAt = clockSeconds();
        var previousRate = -1;
        try
        {
            await foreach (var frame in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var rule = policy.GetRule(key.ClientMac);
                    if (rule.PauseInternet)
                    {
                        frameDropped();
                        continue;
                    }

                    var kiloBytesPerSecond = key.Direction == TrafficDirection.Download
                        ? rule.DownloadKiloBytesPerSecond
                        : rule.UploadKiloBytesPerSecond;
                    var now = clockSeconds();
                    if (kiloBytesPerSecond <= 0)
                    {
                        previousRate = 0;
                        nextSendAt = now;
                    }
                    else
                    {
                        if (kiloBytesPerSecond != previousRate)
                        {
                            previousRate = kiloBytesPerSecond;
                            nextSendAt = now;
                        }

                        if (nextSendAt > now)
                        {
                            await delayAsync(
                                TimeSpan.FromSeconds(nextSendAt - now),
                                cancellationToken);
                        }
                    }

                    rule = policy.GetRule(key.ClientMac);
                    if (rule.PauseInternet)
                    {
                        frameDropped();
                        continue;
                    }

                    sendFrame(frame);
                    if (kiloBytesPerSecond > 0)
                    {
                        var bytesPerSecond = kiloBytesPerSecond * 1_000D;
                        var scheduled = nextSendAt + (frame.Length / bytesPerSecond);
                        // Task.Delay can overshoot short packet intervals. Keep the
                        // original schedule so a small catch-up burst restores the
                        // requested average, but never accumulate over 50 ms of debt.
                        nextSendAt = Math.Max(scheduled, clockSeconds() - 0.05D);
                    }
                }
                finally
                {
                    queue.Release(frame.Length);
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

    private sealed class FrameQueue
    {
        private long queuedBytes;
        private readonly CancellationTokenSource cancellation;
        private int stopped;

        public FrameQueue(LinuxFramePacer owner, QueueKey key)
        {
            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(
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

        public ChannelWriter<byte[]> Writer { get; }
        public Task Worker { get; }

        public bool TryWrite(byte[] frame, long maximumQueuedBytes)
        {
            var total = Interlocked.Add(ref queuedBytes, frame.Length);
            if (total > maximumQueuedBytes || !Writer.TryWrite(frame))
            {
                Interlocked.Add(ref queuedBytes, -frame.Length);
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
