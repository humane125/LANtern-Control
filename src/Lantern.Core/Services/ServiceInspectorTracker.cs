using Lantern.Core.Control;
using Lantern.Core.Networking;

namespace Lantern.Core.Services;

public sealed class ServiceInspectorTracker
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExpirationScanInterval = TimeSpan.FromSeconds(1);

    private readonly object sync = new();
    private readonly Dictionary<ServiceFlowKey, FlowState> flows = [];
    private readonly Dictionary<SessionKey, SessionState> sessions = [];
    private readonly Dictionary<string, RecentServiceContext> recentServiceContexts = [];
    private readonly List<CompletedServiceSession> completed = [];
    private DateTimeOffset nextExpirationScan = DateTimeOffset.MinValue;

    public void Observe(FrameRouteResult result, DateTimeOffset observedAt)
    {
        if (result.ClientMac is null || result.Direction is null)
        {
            return;
        }

        Observe(ServiceInspectorObservation.FromRouteResult(result), observedAt);
    }

    public void Observe(ServiceInspectorObservation result, DateTimeOffset observedAt)
    {
        var macKey = result.Flow?.ClientMac ??
                     TrafficPolicy.NormalizeMac(result.ClientMac.ToString());
        lock (sync)
        {
            ExpireIfDue(observedAt);

            if (result.DomainObservation is { } observation)
            {
                var observedService = ResolveService(
                    macKey,
                    observation.Domain,
                    observedAt);
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
                service = IsSharedMetaCdn(result.AttributedDomain) &&
                          flows.TryGetValue(flow, out var boundFlow)
                    ? boundFlow.Service
                    : ResolveService(
                        macKey,
                        result.AttributedDomain,
                        observedAt);
                if (flows.TryGetValue(flow, out var attributedFlow))
                {
                    attributedFlow.Service = service;
                    attributedFlow.LastActivity = observedAt;
                }
                else
                {
                    flows[flow] = new FlowState(service, observedAt);
                }
            }
            else if (flows.TryGetValue(flow, out var existingFlow))
            {
                service = existingFlow.Service == ServiceDefinitionCatalog.Other &&
                          TryResolveRecentEncryptedMetaContext(
                              macKey,
                              flow,
                              observedAt,
                              out var reboundService)
                    ? reboundService
                    : existingFlow.Service;
                existingFlow.Service = service;
                existingFlow.LastActivity = observedAt;
            }
            else
            {
                service = TryResolveRecentEncryptedMetaContext(
                    macKey,
                    flow,
                    observedAt,
                    out var contextualService)
                    ? contextualService
                    : ServiceDefinitionCatalog.Other;
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
            recentServiceContexts.Clear();
            nextExpirationScan = DateTimeOffset.MinValue;
        }
    }

    private void ExpireIfDue(DateTimeOffset now)
    {
        if (now < nextExpirationScan)
        {
            return;
        }

        Expire(now);
        nextExpirationScan = now + ExpirationScanInterval;
    }

    private ServiceDefinition ResolveService(
        string macKey,
        string domain,
        DateTimeOffset observedAt)
    {
        var matched = ServiceDefinitionCatalog.MatchDomain(domain);
        if (!IsSharedMetaCdn(domain))
        {
            if (IsMetaService(matched))
            {
                recentServiceContexts[macKey] = new RecentServiceContext(
                    matched,
                    observedAt);
            }

            return matched;
        }

        if (recentServiceContexts.TryGetValue(macKey, out var recent) &&
            observedAt - recent.LastActivity < IdleTimeout)
        {
            recentServiceContexts[macKey] = recent with { LastActivity = observedAt };
            return recent.Service;
        }

        return matched;
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
        foreach (var macKey in recentServiceContexts
                     .Where(pair => now - pair.Value.LastActivity >= IdleTimeout)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            recentServiceContexts.Remove(macKey);
        }

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

    private readonly record struct RecentServiceContext(
        ServiceDefinition Service,
        DateTimeOffset LastActivity);

    private sealed class FlowState(
        ServiceDefinition service,
        DateTimeOffset lastActivity)
    {
        public ServiceDefinition Service { get; set; } = service;

        public DateTimeOffset LastActivity { get; set; } = lastActivity;
    }

    private static bool IsMetaService(ServiceDefinition service) =>
        service.Id is "facebook" or "instagram" or "messenger";

    private bool TryResolveRecentEncryptedMetaContext(
        string macKey,
        ServiceFlowKey flow,
        DateTimeOffset observedAt,
        out ServiceDefinition service)
    {
        if (flow.RemotePort == 443 &&
            flow.Protocol is 6 or 17 &&
            recentServiceContexts.TryGetValue(macKey, out var recent) &&
            observedAt - recent.LastActivity < IdleTimeout)
        {
            service = recent.Service;
            return true;
        }

        service = ServiceDefinitionCatalog.Other;
        return false;
    }

    private static bool IsSharedMetaCdn(string domain)
    {
        var normalized = domain.Trim().TrimEnd('.');
        return normalized.Equals("fbcdn.net", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase);
    }

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
