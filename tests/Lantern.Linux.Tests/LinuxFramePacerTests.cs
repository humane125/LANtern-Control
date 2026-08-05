using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Linux.Services;

namespace Lantern.Linux.Tests;

public sealed class LinuxFramePacerTests
{
    private static readonly PhysicalAddress ClientMac =
        PhysicalAddress.Parse("E261190DBD54");

    [Fact]
    public async Task UnlimitedFrames_AreSentInArrivalOrder()
    {
        var sent = new ConcurrentQueue<byte>();
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pacer = new LinuxFramePacer(
            new TrafficPolicy(),
            frame =>
            {
                sent.Enqueue(frame[0]);
                if (sent.Count == 3)
                {
                    completed.TrySetResult();
                }
            });

        Assert.True(pacer.TryEnqueue(ClientMac, TrafficDirection.Upload, [1]));
        Assert.True(pacer.TryEnqueue(ClientMac, TrafficDirection.Upload, [2]));
        Assert.True(pacer.TryEnqueue(ClientMac, TrafficDirection.Upload, [3]));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new byte[] { 1, 2, 3 }, sent);
    }

    [Fact]
    public async Task LimitedFrames_ArePacedWithoutDroppingTcpData()
    {
        var clock = new ManualClock();
        var delays = new ConcurrentQueue<TimeSpan>();
        var sent = 0;
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new TrafficPolicy();
        policy.SetRule(ClientMac.ToString(), new TrafficRule(false, 0, 1));
        await using var pacer = new LinuxFramePacer(
            policy,
            _ =>
            {
                if (Interlocked.Increment(ref sent) == 2)
                {
                    completed.TrySetResult();
                }
            },
            clock.Read,
            (delay, _) =>
            {
                delays.Enqueue(delay);
                clock.Advance(delay.TotalSeconds);
                return Task.CompletedTask;
            });

        Assert.True(pacer.TryEnqueue(ClientMac, TrafficDirection.Upload, new byte[1_000]));
        Assert.True(pacer.TryEnqueue(ClientMac, TrafficDirection.Upload, new byte[1_000]));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var delay = Assert.Single(delays);
        Assert.InRange(delay.TotalSeconds, 0.999, 1.001);
        Assert.Equal(2, sent);
    }

    [Fact]
    public async Task ServiceLimitedFrames_ArePacedInsideAnUnlimitedDevice()
    {
        var clock = new ManualClock();
        var delays = new ConcurrentQueue<TimeSpan>();
        var sent = 0;
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new TrafficPolicy();
        policy.SetServiceRule(
            ClientMac.ToString(),
            "youtube",
            new ServiceTrafficRule(0, 1));
        await using var pacer = new LinuxFramePacer(
            policy,
            _ =>
            {
                if (Interlocked.Increment(ref sent) == 2)
                {
                    completed.TrySetResult();
                }
            },
            clock.Read,
            (delay, _) =>
            {
                delays.Enqueue(delay);
                clock.Advance(delay.TotalSeconds);
                return Task.CompletedTask;
            });

        Assert.True(pacer.TryEnqueue(
            ClientMac, "youtube", TrafficDirection.Upload, new byte[1_000]));
        Assert.True(pacer.TryEnqueue(
            ClientMac, "youtube", TrafficDirection.Upload, new byte[1_000]));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var delay = Assert.Single(delays);
        Assert.InRange(delay.TotalSeconds, 0.999, 1.001);
        Assert.Equal(2, sent);
    }

    [Fact]
    public async Task LimitedQueue_RejectsADeepBurstBeforeItCreatesTcpTimeouts()
    {
        var policy = new TrafficPolicy();
        policy.SetRule(ClientMac.ToString(), new TrafficRule(false, 0, 1));
        await using var pacer = new LinuxFramePacer(
            policy,
            _ => { },
            delayAsync: (_, cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        var accepted = Enumerable.Range(0, 20)
            .Count(_ => pacer.TryEnqueue(
                ClientMac,
                TrafficDirection.Upload,
                new byte[1_500]));

        Assert.InRange(accepted, 1, 11);
    }

    [Fact]
    public async Task ResetAsync_CancelsOldBacklogSoUnlimitedTrafficResumesCleanly()
    {
        var sentMarker = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new TrafficPolicy();
        policy.SetRule(ClientMac.ToString(), new TrafficRule(false, 0, 1));
        await using var pacer = new LinuxFramePacer(
            policy,
            frame =>
            {
                if (frame[0] == 99)
                {
                    sentMarker.TrySetResult();
                }
            },
            delayAsync: (_, cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        Assert.True(pacer.TryEnqueue(
            ClientMac,
            TrafficDirection.Upload,
            new byte[1_000]));
        Assert.True(pacer.TryEnqueue(
            ClientMac,
            TrafficDirection.Upload,
            new byte[1_000]));

        policy.SetRule(ClientMac.ToString(), new TrafficRule(false, 0, 0));
        await pacer.ResetAsync(ClientMac);
        Assert.True(pacer.TryEnqueue(
            ClientMac,
            TrafficDirection.Upload,
            new byte[] { 99 }));

        await sentMarker.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ManualClock
    {
        private double seconds;

        public double Read() => Volatile.Read(ref seconds);

        public void Advance(double value)
        {
            var current = Volatile.Read(ref seconds);
            Volatile.Write(ref seconds, current + value);
        }
    }
}
