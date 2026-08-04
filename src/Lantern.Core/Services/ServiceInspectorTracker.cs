using Lantern.Core.Control;
using Lantern.Core.Networking;

namespace Lantern.Core.Services;

public sealed class ServiceInspectorTracker
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    private readonly object sync = new();
    private readonly Dictionary<ServiceFlowKey, FlowState> flows = [];
    private readonly Dictionary<SessionKey, SessionState> sessions = [];
    private readonly List<CompletedServiceSession> completed = [];

    public void Observe(FrameRouteResult result, DateTimeOffset observedAt)
    {
        if (result.ClientMac is null || result.Direction is null)
        {
            return;
        }

        var macKey = TrafficPolicy.NormalizeMac(result.ClientMac.ToString());
        lock (sync)
        {
            Expire(observedAt);

            if (result.Observation is { } observation)
            {
                var observedService = ServiceDefinitionCatalog.MatchDomain(observation.Domain);
                if (observedService != ServiceDefinitionCatalog.Other)
                {
                    _ = GetOrCreateSession(macKey, observedService, observedAt);
                }
            }

            if (result.Flow is not { } flow)
            {
                return;
            }

            ServiceDefinition service;
            if (!string.IsNullOrWhiteSpace(result.AttributedDomain))
            {
                service = ServiceDefinitionCatalog.MatchDomain(result.AttributedDomain);
                flows[flow] = new FlowState(service, observedAt);
            }
            else if (flows.TryGetValue(flow, out var existingFlow))
            {
                service = existingFlow.Service;
                flows[flow] = existingFlow with { LastActivity = observedAt };
            }
            else
            {
                service = ServiceDefinitionCatalog.Other;
                flows[flow] = new FlowState(service, observedAt);
            }

            var session = GetOrCreateSession(macKey, service, observedAt);
            session.LastActivity = observedAt;
            session.ObservedFlows.Add(flow);
            var byteCount = Math.Max(0, result.MeteredByteCount);
            if (result.Direction == TrafficDirection.Download)
            {
                session.DownloadBytes += byteCount;
            }
            else
            {
                session.UploadBytes += byteCount;
            }
        }
    }

    public IReadOnlyList<ServiceSessionSnapshot> GetSnapshots(DateTimeOffset sampledAt)
    {
        lock (sync)
        {
            Expire(sampledAt);
            var snapshots = new List<ServiceSessionSnapshot>(sessions.Count);
            foreach (var session in sessions.Values)
            {
                var elapsedSeconds = Math.Max(
                    0,
                    (sampledAt - session.LastSampleAt).TotalSeconds);
                var downloadRate = elapsedSeconds > 0
                    ? (session.DownloadBytes - session.LastSampleDownloadBytes) / elapsedSeconds
                    : 0;
                var uploadRate = elapsedSeconds > 0
                    ? (session.UploadBytes - session.LastSampleUploadBytes) / elapsedSeconds
                    : 0;
                session.LastSampleAt = sampledAt;
                session.LastSampleDownloadBytes = session.DownloadBytes;
                session.LastSampleUploadBytes = session.UploadBytes;

                snapshots.Add(new ServiceSessionSnapshot(
                    session.MacKey,
                    session.Service.Id,
                    session.Service.Name,
                    session.StartedAt,
                    session.LastActivity,
                    sampledAt - session.StartedAt,
                    session.DownloadBytes,
                    session.UploadBytes,
                    downloadRate,
                    uploadRate,
                    session.ObservedFlows.Count(flow =>
                        flows.TryGetValue(flow, out var state) &&
                        sampledAt - state.LastActivity < IdleTimeout),
                    true));
            }

            return snapshots
                .OrderBy(snapshot => snapshot.MacKey, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(snapshot =>
                    snapshot.DownloadBytesPerSecond + snapshot.UploadBytesPerSecond)
                .ThenBy(snapshot => snapshot.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<CompletedServiceSession> DrainCompletedSessions(
        DateTimeOffset sampledAt)
    {
        lock (sync)
        {
            Expire(sampledAt);
            var result = completed.ToArray();
            completed.Clear();
            return result;
        }
    }

    public void CompleteAll(DateTimeOffset stoppedAt)
    {
        lock (sync)
        {
            foreach (var session in sessions.Values)
            {
                completed.Add(ToCompleted(session, stoppedAt));
            }

            sessions.Clear();
            flows.Clear();
        }
    }

    private SessionState GetOrCreateSession(
        string macKey,
        ServiceDefinition service,
        DateTimeOffset observedAt)
    {
        var key = new SessionKey(macKey, service.Id);
        if (sessions.TryGetValue(key, out var existing))
        {
            existing.LastActivity = observedAt;
            return existing;
        }

        var session = new SessionState(macKey, service, observedAt);
        sessions[key] = session;
        return session;
    }

    private void Expire(DateTimeOffset now)
    {
        foreach (var flow in flows
                     .Where(pair => now - pair.Value.LastActivity >= IdleTimeout)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            flows.Remove(flow);
        }

        foreach (var pair in sessions
                     .Where(pair => now - pair.Value.LastActivity >= IdleTimeout)
                     .ToArray())
        {
            completed.Add(ToCompleted(pair.Value, pair.Value.LastActivity + IdleTimeout));
            sessions.Remove(pair.Key);
        }
    }

    private static CompletedServiceSession ToCompleted(
        SessionState session,
        DateTimeOffset endedAt) =>
        new(
            session.MacKey,
            session.Service.Id,
            session.Service.Name,
            session.StartedAt,
            endedAt,
            session.DownloadBytes,
            session.UploadBytes,
            session.ObservedFlows.Count);

    private readonly record struct SessionKey(string MacKey, string ServiceId);

    private sealed record FlowState(
        ServiceDefinition Service,
        DateTimeOffset LastActivity);

    private sealed class SessionState(
        string macKey,
        ServiceDefinition service,
        DateTimeOffset startedAt)
    {
        public string MacKey { get; } = macKey;

        public ServiceDefinition Service { get; } = service;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public DateTimeOffset LastActivity { get; set; } = startedAt;

        public DateTimeOffset LastSampleAt { get; set; } = startedAt;

        public long DownloadBytes { get; set; }

        public long UploadBytes { get; set; }

        public long LastSampleDownloadBytes { get; set; }

        public long LastSampleUploadBytes { get; set; }

        public HashSet<ServiceFlowKey> ObservedFlows { get; } = [];
    }
}
