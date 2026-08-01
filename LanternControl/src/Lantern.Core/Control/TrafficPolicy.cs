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
        _ = GetRule(macAddress);
        return InterceptionTargets.Client | InterceptionTargets.Gateway;
    }

    public void RemoveRule(string macAddress) => rules.TryRemove(NormalizeMac(macAddress), out _);

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

    private sealed record DeviceLimiters(
        TrafficRule Rule,
        TokenBucket Download,
        TokenBucket Upload);
}
