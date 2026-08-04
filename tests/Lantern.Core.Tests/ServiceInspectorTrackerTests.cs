using System.Net;
using System.Net.NetworkInformation;
using Lantern.Core.Control;
using Lantern.Core.Networking;
using Lantern.Core.Services;

namespace Lantern.Core.Tests;

public sealed class ServiceInspectorTrackerTests
{
    private const string MacKey = "E261190DBD54";
    private static readonly PhysicalAddress Mac = PhysicalAddress.Parse(MacKey);
    private static readonly DateTimeOffset Start =
        new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snapshot_AccountsBothDirectionsAndRateDelta()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(Result("youtube.com", TrafficDirection.Upload, 1_000), Start);
        tracker.Observe(Result("youtube.com", TrafficDirection.Download, 4_000), Start.AddSeconds(1));

        var snapshot = Assert.Single(tracker.GetSnapshots(Start.AddSeconds(2.5)));

        Assert.Equal("youtube", snapshot.ServiceId);
        Assert.Equal(1_000, snapshot.UploadBytes);
        Assert.Equal(4_000, snapshot.DownloadBytes);
        Assert.Equal(400, snapshot.UploadBytesPerSecond);
        Assert.Equal(1_600, snapshot.DownloadBytesPerSecond);
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.True(snapshot.IsActive);
    }

    [Fact]
    public void Snapshot_UsesDeltasInsteadOfLifetimeAverage()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(Result("discord.com", TrafficDirection.Download, 2_500), Start);
        _ = tracker.GetSnapshots(Start.AddSeconds(2.5));
        tracker.Observe(Result("discord.com", TrafficDirection.Download, 5_000), Start.AddSeconds(4));

        var snapshot = Assert.Single(tracker.GetSnapshots(Start.AddSeconds(5)));

        Assert.Equal(2_000, snapshot.DownloadBytesPerSecond);
        Assert.Equal(7_500, snapshot.DownloadBytes);
    }

    [Fact]
    public void Observe_AfterSixtySeconds_CompletesOldSessionAndCreatesNewOne()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(Result("youtube.com", TrafficDirection.Upload, 1_000), Start);

        Assert.Empty(tracker.GetSnapshots(Start.AddSeconds(60)));
        tracker.Observe(Result("youtube.com", TrafficDirection.Upload, 500), Start.AddSeconds(61));

        var completed = Assert.Single(tracker.DrainCompletedSessions(Start.AddSeconds(61)));
        Assert.Equal(1_000, completed.UploadBytes);
        Assert.Equal(Start, completed.StartedAt);
        Assert.Equal(Start.AddSeconds(60), completed.EndedAt);
        var current = Assert.Single(tracker.GetSnapshots(Start.AddSeconds(61)));
        Assert.Equal(500, current.UploadBytes);
        Assert.Equal(Start.AddSeconds(61), current.FirstSeen);
    }

    [Fact]
    public void ConcurrentServicesAndFlows_RemainSeparate()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(Result("youtube.com", TrafficDirection.Download, 1_000, 50001), Start);
        tracker.Observe(Result("googlevideo.com", TrafficDirection.Download, 2_000, 50002), Start);
        tracker.Observe(Result("discord.com", TrafficDirection.Upload, 3_000, 50003), Start);

        var snapshots = tracker.GetSnapshots(Start.AddSeconds(2.5));

        var youtube = Assert.Single(snapshots, item => item.ServiceId == "youtube");
        Assert.Equal(3_000, youtube.DownloadBytes);
        Assert.Equal(2, youtube.ActiveConnections);
        var discord = Assert.Single(snapshots, item => item.ServiceId == "discord");
        Assert.Equal(3_000, discord.UploadBytes);
        Assert.Equal(1, discord.ActiveConnections);
    }

    [Fact]
    public void DnsObservation_OpensNamedSessionWithoutAssigningResolverBytes()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(
            new FrameRouteResult(
                FrameAction.Forward,
                TrafficDirection.Upload,
                Mac,
                MeteredByteCount: 80,
                Observation: new DomainObservation(
                    "youtube.com",
                    DomainObservationSource.Dns,
                    IPAddress.Parse("1.1.1.1"))),
            Start);

        var snapshot = Assert.Single(tracker.GetSnapshots(Start.AddSeconds(2.5)));

        Assert.Equal("youtube", snapshot.ServiceId);
        Assert.Equal(0, snapshot.UploadBytes);
        Assert.Equal(0, snapshot.DownloadBytes);
        Assert.Equal(0, snapshot.ActiveConnections);
    }

    [Fact]
    public void UnknownAttributedFlow_IsAccountedAsOtherWithoutGuessing()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(Result(null, TrafficDirection.Download, 900), Start);

        var snapshot = Assert.Single(tracker.GetSnapshots(Start.AddSeconds(2.5)));

        Assert.Equal("other", snapshot.ServiceId);
        Assert.Equal("Other", snapshot.ServiceName);
        Assert.Equal(900, snapshot.DownloadBytes);
    }

    [Fact]
    public void SharedMetaCdn_UsesRecentInstagramContextInsteadOfFacebook()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(
            new FrameRouteResult(
                FrameAction.Forward,
                TrafficDirection.Upload,
                Mac,
                Observation: new DomainObservation(
                    "i.instagram.com",
                    DomainObservationSource.Dns,
                    IPAddress.Parse("1.1.1.1"))),
            Start);
        tracker.Observe(
            Result("video.xx.fbcdn.net", TrafficDirection.Download, 5_000),
            Start.AddSeconds(1));

        var snapshots = tracker.GetSnapshots(Start.AddSeconds(2.5));

        var instagram = Assert.Single(snapshots, item => item.ServiceId == "instagram");
        Assert.Equal(5_000, instagram.DownloadBytes);
        Assert.DoesNotContain(snapshots, item => item.ServiceId == "facebook");
    }

    [Fact]
    public void SharedMetaCdn_KeepsExistingFlowBoundWhenContextChanges()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(DnsObservation("i.instagram.com"), Start);
        tracker.Observe(
            Result("video.xx.fbcdn.net", TrafficDirection.Download, 1_000, 50001),
            Start.AddSeconds(1));
        tracker.Observe(DnsObservation("graph.facebook.com"), Start.AddSeconds(2));
        tracker.Observe(
            Result("video.xx.fbcdn.net", TrafficDirection.Download, 2_000, 50001),
            Start.AddSeconds(3));
        tracker.Observe(
            Result("video.xx.fbcdn.net", TrafficDirection.Download, 3_000, 50002),
            Start.AddSeconds(3));

        var snapshots = tracker.GetSnapshots(Start.AddSeconds(4));

        Assert.Equal(
            3_000,
            Assert.Single(snapshots, item => item.ServiceId == "instagram").DownloadBytes);
        Assert.Equal(
            3_000,
            Assert.Single(snapshots, item => item.ServiceId == "facebook").DownloadBytes);
    }

    [Fact]
    public void CompleteAll_CheckpointsEveryOpenSessionExactlyOnce()
    {
        var tracker = new ServiceInspectorTracker();
        tracker.Observe(Result("youtube.com", TrafficDirection.Download, 100), Start);
        tracker.Observe(Result("discord.com", TrafficDirection.Upload, 200, 50002), Start);

        tracker.CompleteAll(Start.AddSeconds(10));
        var completed = tracker.DrainCompletedSessions(Start.AddSeconds(10));

        Assert.Equal(2, completed.Count);
        Assert.Empty(tracker.DrainCompletedSessions(Start.AddSeconds(10)));
        Assert.Empty(tracker.GetSnapshots(Start.AddSeconds(10)));
    }

    private static FrameRouteResult Result(
        string? domain,
        TrafficDirection direction,
        int bytes,
        ushort clientPort = 50001)
    {
        var remote = IPAddress.Parse("142.250.186.110");
        return new FrameRouteResult(
            FrameAction.Forward,
            direction,
            Mac,
            MeteredByteCount: bytes,
            Flow: new ServiceFlowKey(MacKey, clientPort, remote, 443, 6),
            AttributedDomain: domain);
    }

    private static FrameRouteResult DnsObservation(string domain) =>
        new(
            FrameAction.Forward,
            TrafficDirection.Upload,
            Mac,
            Observation: new DomainObservation(
                domain,
                DomainObservationSource.Dns,
                IPAddress.Parse("1.1.1.1")));
}
