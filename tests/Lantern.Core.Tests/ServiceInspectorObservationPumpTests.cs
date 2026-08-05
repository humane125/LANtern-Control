using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Networking;
using Lantern.Core.Services;

namespace Lantern.Core.Tests;

public sealed class ServiceInspectorObservationPumpTests
{
    [Fact]
    public async Task TryObserve_DoesNotWaitForSlowInspectorProcessing()
    {
        using var observerEntered = new ManualResetEventSlim();
        using var releaseObserver = new ManualResetEventSlim();
        ServiceInspectorObservationPump? pump = null;
        try
        {
            pump = new ServiceInspectorObservationPump((_, _) =>
            {
                observerEntered.Set();
                releaseObserver.Wait(TimeSpan.FromSeconds(5));
            });

            Assert.True(pump.TryObserve(Result(), DateTimeOffset.UtcNow));
            Assert.True(observerEntered.Wait(TimeSpan.FromSeconds(1)));

            var secondWrite = Task.Run(() =>
                pump.TryObserve(Result(), DateTimeOffset.UtcNow));

            Assert.True(await secondWrite.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            releaseObserver.Set();
            if (pump is not null)
            {
                await pump.DisposeAsync();
            }
        }
    }

    private static FrameRouteResult Result() =>
        new(
            FrameAction.Forward,
            TrafficDirection.Download,
            PhysicalAddress.Parse("0E4F69CCE4F0"),
            MeteredByteCount: 1_500,
            Flow: new ServiceFlowKey(
                "0E4F69CCE4F0",
                50_000,
                System.Net.IPAddress.Parse("1.1.1.1"),
                443,
                6),
            AttributedDomain: "youtube.com",
            ServiceId: "youtube");
}
