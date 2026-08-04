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
}
