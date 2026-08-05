using System.Collections.Concurrent;
using Lantern.Core.Services;

namespace Lantern.Core.Control;

[Flags]
public enum InterceptionTargets
{
    None = 0,
    Client = 1,
    Gateway = 2,
}

public sealed class TrafficPolicy
{
    private readonly ConcurrentDictionary<string, DeviceLimiters> rules =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string[]> blockedDomains =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ServiceRuleKey, ServiceLimiters> serviceRules = [];
    private readonly Func<double>? clockSeconds;
    private int safeModeEnabled;

    public TrafficPolicy(Func<double>? clockSeconds = null)
    {
        this.clockSeconds = clockSeconds;
    }

    public void SetRule(string macAddress, TrafficRule rule)
    {
        var normalized = rule.Normalize();
        rules[NormalizeMac(macAddress)] = new DeviceLimiters(
            normalized,
            new TokenBucket(normalized.DownloadKiloBytesPerSecond * 1_000D, clockSeconds),
            new TokenBucket(normalized.UploadKiloBytesPerSecond * 1_000D, clockSeconds));
    }

    public TrafficRule GetRule(string macAddress) =>
        rules.TryGetValue(NormalizeMac(macAddress), out var limiters)
            ? limiters.Rule
            : new TrafficRule(false, 0, 0);

    public bool RequiresInterception(string macAddress)
    {
        return GetInterceptionTargets(macAddress) != InterceptionTargets.None;
    }

    public InterceptionTargets GetInterceptionTargets(string macAddress)
    {
        var macKey = NormalizeMac(macAddress);
        if (!SafeModeEnabled)
        {
            return InterceptionTargets.Client | InterceptionTargets.Gateway;
        }

        var rule = GetRule(macKey);
        var requiresEnforcement =
            rule.PauseInternet ||
            rule.DownloadKiloBytesPerSecond > 0 ||
            rule.UploadKiloBytesPerSecond > 0 ||
            blockedDomains.ContainsKey(macKey) ||
            serviceRules.Any(pair =>
                pair.Key.MacKey.Equals(macKey, StringComparison.OrdinalIgnoreCase) &&
                !pair.Value.Rule.IsUnlimited);
        if (!requiresEnforcement)
        {
            return InterceptionTargets.None;
        }

        return InterceptionTargets.Client | InterceptionTargets.Gateway;
    }

    public bool SafeModeEnabled => Volatile.Read(ref safeModeEnabled) != 0;

    public void SetSafeMode(bool enabled) =>
        Volatile.Write(ref safeModeEnabled, enabled ? 1 : 0);

    public void SetServiceRule(
        string macAddress,
        string serviceId,
        ServiceTrafficRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var key = new ServiceRuleKey(
            NormalizeMac(macAddress),
            NormalizeServiceId(serviceId));
        var normalized = rule.Normalize();
        if (normalized.IsUnlimited)
        {
            serviceRules.TryRemove(key, out _);
            return;
        }

        serviceRules[key] = new ServiceLimiters(
            normalized,
            new TokenBucket(normalized.DownloadKiloBytesPerSecond * 1_000D, clockSeconds),
            new TokenBucket(normalized.UploadKiloBytesPerSecond * 1_000D, clockSeconds));
    }

    public ServiceTrafficRule GetServiceRule(string macAddress, string serviceId) =>
        serviceRules.TryGetValue(
            new ServiceRuleKey(NormalizeMac(macAddress), NormalizeServiceId(serviceId)),
            out var rule)
            ? rule.Rule
            : new ServiceTrafficRule(0, 0);

    public IReadOnlyDictionary<string, ServiceTrafficRule> GetServiceRules(string macAddress)
    {
        var macKey = NormalizeMac(macAddress);
        return serviceRules
            .Where(pair => pair.Key.MacKey.Equals(macKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                pair => pair.Key.ServiceId,
                pair => pair.Value.Rule,
                StringComparer.OrdinalIgnoreCase);
    }

    public void RemoveRule(string macAddress) => rules.TryRemove(NormalizeMac(macAddress), out _);

    public void SetBlockedDomains(string macAddress, IEnumerable<string> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        var macKey = NormalizeMac(macAddress);
        var normalized = domains
            .Select(domain => TryNormalizeDomain(domain, out var value) ? value : null)
            .Where(domain => domain is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
        {
            blockedDomains.TryRemove(macKey, out _);
            return;
        }

        blockedDomains[macKey] = normalized;
    }

    public IReadOnlyList<string> GetBlockedDomains(string macAddress) =>
        blockedDomains.TryGetValue(NormalizeMac(macAddress), out var domains)
            ? domains
            : [];

    public bool ShouldBlockDomain(string macAddress, string domain)
    {
        if (!TryNormalizeDomain(domain, out var normalized) ||
            !blockedDomains.TryGetValue(NormalizeMac(macAddress), out var domains))
        {
            return false;
        }

        return domains.Any(blocked =>
            normalized.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith($".{blocked}", StringComparison.OrdinalIgnoreCase));
    }

    public bool ShouldForward(string macAddress, TrafficDirection direction, int byteCount)
    {
        return ShouldForward(macAddress, null, direction, byteCount);
    }

    public bool ShouldForward(
        string macAddress,
        string? serviceId,
        TrafficDirection direction,
        int byteCount)
    {
        var macKey = NormalizeMac(macAddress);
        var limiters = rules.GetOrAdd(
            macKey,
            _ => CreateDeviceLimiters(new TrafficRule(false, 0, 0)));

        if (limiters.Rule.PauseInternet)
        {
            return false;
        }

        var parent = direction == TrafficDirection.Download
            ? limiters.Download
            : limiters.Upload;
        if (string.IsNullOrWhiteSpace(serviceId) ||
            !serviceRules.TryGetValue(
                new ServiceRuleKey(macKey, serviceId.Trim().ToLowerInvariant()),
                out var serviceLimiters))
        {
            return parent.TryConsume(byteCount);
        }

        var child = direction == TrafficDirection.Download
            ? serviceLimiters.Download
            : serviceLimiters.Upload;
        return TokenBucket.TryConsumeBoth(parent, child, byteCount);
    }

    public static string NormalizeMac(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length != 12)
        {
            throw new FormatException("A MAC address must contain 12 hexadecimal digits.");
        }

        return normalized;
    }

    public static string NormalizeDomain(string value)
    {
        if (!TryNormalizeDomain(value, out var normalized))
        {
            throw new FormatException("Enter a valid domain such as example.com.");
        }

        return normalized;
    }

    private static bool TryNormalizeDomain(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (value.StartsWith("*.", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value.Length is 0 or > 253 ||
            !value.Contains('.', StringComparison.Ordinal) ||
            Uri.CheckHostName(value) != UriHostNameType.Dns)
        {
            return false;
        }

        normalized = value;
        return true;
    }

    private sealed record DeviceLimiters(
        TrafficRule Rule,
        TokenBucket Download,
        TokenBucket Upload);

    private sealed record ServiceLimiters(
        ServiceTrafficRule Rule,
        TokenBucket Download,
        TokenBucket Upload);

    private DeviceLimiters CreateDeviceLimiters(TrafficRule rule)
    {
        var normalized = rule.Normalize();
        return new DeviceLimiters(
            normalized,
            new TokenBucket(normalized.DownloadKiloBytesPerSecond * 1_000D, clockSeconds),
            new TokenBucket(normalized.UploadKiloBytesPerSecond * 1_000D, clockSeconds));
    }

    private static string NormalizeServiceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!ServiceDefinitionCatalog.All.Any(service =>
                service.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Unknown service ID '{value}'.", nameof(value));
        }

        return normalized;
    }

    private readonly record struct ServiceRuleKey(string MacKey, string ServiceId);
}
