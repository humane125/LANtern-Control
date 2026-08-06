using SharpPcap;
using Lantern.Core.Networking;

namespace Lantern.App.Services;

public static class ForwarderStartupPolicy
{
    public static void ObserveFirstRead(
        TaskCompletionSource ready,
        GetPacketStatus status,
        string? lastError)
    {
        if (status == GetPacketStatus.Error)
        {
            var exception = new InvalidOperationException(
                $"Packet forwarding stopped: {lastError}");
            ready.TrySetException(exception);
            throw exception;
        }

        ready.TrySetResult();
    }
}

public static class PacketInjectionPolicy
{
    public static IReadOnlyList<byte[]> PrepareFrames(ReadOnlySpan<byte> capturedFrame) =>
        Ipv4FrameNormalizer.Normalize(capturedFrame);

    public static T SelectHandle<T>(T? establishedCaptureHandle, T? secondaryHandle)
        where T : class =>
        establishedCaptureHandle ?? secondaryHandle ??
        throw new InvalidOperationException("No packet injection adapter is open.");

    public static T SendUsingFirstWorkingHandle<T>(
        IEnumerable<T?> handles,
        Action<T> send)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(handles);
        ArgumentNullException.ThrowIfNull(send);

        Exception? lastFailure = null;
        var attempted = new HashSet<T>(ReferenceEqualityComparer.Instance);
        foreach (var handle in handles)
        {
            if (handle is null || !attempted.Add(handle))
            {
                continue;
            }

            try
            {
                send(handle);
                return handle;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }

        if (lastFailure is null)
        {
            throw new InvalidOperationException("No packet injection adapter is open.");
        }

        throw new InvalidOperationException(
            "Npcap rejected packet injection through every open adapter handle: " +
            lastFailure.Message,
            lastFailure);
    }
}
