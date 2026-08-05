using System.Threading.Channels;
using Lantern.Core.Networking;

namespace Lantern.Core.Services;

public sealed class ServiceInspectorObservationPump : IAsyncDisposable
{
    private const int DefaultCapacity = 16_384;
    private readonly Channel<QueuedObservation> observations;
    private readonly Action<ServiceInspectorObservation, DateTimeOffset> observer;
    private readonly Task worker;
    private int accepting = 1;
    private long droppedObservationCount;

    public ServiceInspectorObservationPump(
        Action<ServiceInspectorObservation, DateTimeOffset> observer,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(observer);
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.observer = observer;
        observations = Channel.CreateBounded<QueuedObservation>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        worker = Task.Run(ProcessAsync);
    }

    public long DroppedObservationCount => Interlocked.Read(ref droppedObservationCount);

    public bool TryObserve(FrameRouteResult result, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Volatile.Read(ref accepting) == 0 ||
            result.ClientMac is null ||
            result.Direction is null)
        {
            return false;
        }

        var queued = new QueuedObservation(
            ServiceInspectorObservation.FromRouteResult(result),
            observedAt);
        if (observations.Writer.TryWrite(queued))
        {
            return true;
        }

        Interlocked.Increment(ref droppedObservationCount);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref accepting, 0) == 0)
        {
            await worker.ConfigureAwait(false);
            return;
        }

        observations.Writer.TryComplete();
        await worker.ConfigureAwait(false);
    }

    private async Task ProcessAsync()
    {
        await foreach (var queued in observations.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                observer(queued.Observation, queued.ObservedAt);
            }
            catch (Exception)
            {
                // Service usage is optional telemetry and must never stop forwarding.
            }
        }
    }

    private readonly record struct QueuedObservation(
        ServiceInspectorObservation Observation,
        DateTimeOffset ObservedAt);
}
