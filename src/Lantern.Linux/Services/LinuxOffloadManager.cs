using System.ComponentModel;
using System.Diagnostics;

namespace Lantern.Linux.Services;

public static class LinuxOffloadManager
{
    private static readonly (string EttoolName, string SwitchName)[] ManagedFeatures =
    [
        ("tcp-segmentation-offload", "tso"),
        ("generic-segmentation-offload", "gso"),
        ("generic-receive-offload", "gro"),
        ("large-receive-offload", "lro"),
        ("rx-gro-list", "rx-gro-list"),
        ("rx-udp-gro-forwarding", "rx-udp-gro-forwarding"),
    ];

    public static IReadOnlyList<string> ParseEnabledFeatures(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);
        return ManagedFeatures
            .Where(feature => lines.Any(line =>
                line.Equals(
                    $"{feature.EttoolName}: on",
                    StringComparison.OrdinalIgnoreCase)))
            .Select(feature => feature.SwitchName)
            .ToArray();
    }

    public static async Task<LinuxOffloadSession> DisableAsync(
        string interfaceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        var query = await RunEttoolAsync(["-k", interfaceName], cancellationToken);
        EnsureSuccess(query, "read", interfaceName);
        var enabled = ParseEnabledFeatures(query.StandardOutput);
        var changed = new List<string>();
        try
        {
            foreach (var feature in enabled)
            {
                var result = await RunEttoolAsync(
                    ["-K", interfaceName, feature, "off"],
                    cancellationToken);
                EnsureSuccess(result, $"disable {feature} on", interfaceName);
                changed.Add(feature);
            }

            var verification = await RunEttoolAsync(["-k", interfaceName], cancellationToken);
            EnsureSuccess(verification, "verify", interfaceName);
            var remaining = ParseEnabledFeatures(verification.StandardOutput)
                .Where(enabled.Contains)
                .ToArray();
            if (remaining.Length > 0)
            {
                throw new InvalidOperationException(
                    $"ethtool could not disable {string.Join(", ", remaining)} on '{interfaceName}'.");
            }

            return new LinuxOffloadSession(interfaceName, changed);
        }
        catch
        {
            await RestoreAsync(interfaceName, changed, CancellationToken.None);
            throw;
        }
    }

    internal static async Task RestoreAsync(
        string interfaceName,
        IReadOnlyList<string> features,
        CancellationToken cancellationToken)
    {
        List<string>? errors = null;
        foreach (var feature in features.Reverse())
        {
            var result = await RunEttoolAsync(
                ["-K", interfaceName, feature, "on"],
                cancellationToken);
            if (result.ExitCode != 0)
            {
                errors ??= [];
                errors.Add($"{feature}: {ReadError(result)}");
            }
        }

        if (errors is { Count: > 0 })
        {
            throw new InvalidOperationException(
                $"Could not restore adapter offloads on '{interfaceName}': {string.Join("; ", errors)}");
        }
    }

    private static async Task<EttoolResult> RunEttoolAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ethtool",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("The ethtool process did not start.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new EttoolResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "The bundled ethtool helper could not be started.",
                exception);
        }
    }

    private static void EnsureSuccess(
        EttoolResult result,
        string operation,
        string interfaceName)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not {operation} adapter offloads for '{interfaceName}': {ReadError(result)}");
        }
    }

    private static string ReadError(EttoolResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError)
            ? $"ethtool exited with code {result.ExitCode}"
            : result.StandardError.Trim();

    private sealed record EttoolResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

public sealed class LinuxOffloadSession(
    string interfaceName,
    IReadOnlyList<string> disabledFeatures) : IAsyncDisposable
{
    private int restored;

    public IReadOnlyList<string> DisabledFeatures { get; } = disabledFeatures;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref restored, 1) != 0)
        {
            return;
        }

        await LinuxOffloadManager.RestoreAsync(
            interfaceName,
            DisabledFeatures,
            CancellationToken.None);
    }
}
