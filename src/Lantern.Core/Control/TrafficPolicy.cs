using System.Collections.Concurrent;

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
    private readonly Func<double>? clockSeconds;

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
        _ = NormalizeMac(macAddress);
        return InterceptionTargets.Client | InterceptionTargets.Gateway;
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
        if (!rules.TryGetValue(NormalizeMac(macAddress), out var limiters))
        {
            return true;
        }

        if (limiters.Rule.PauseInternet)
        {
            return false;
        }

        return direction == TrafficDirection.Download
            ? limiters.Download.TryConsume(byteCount)
            : limiters.Upload.TryConsume(byteCount);
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
}
