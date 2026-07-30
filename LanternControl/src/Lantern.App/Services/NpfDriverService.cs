using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Lantern.App.Services;

public static class NpfDriverService
{
    public static async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator())
        {
            throw new InvalidOperationException(
                "Administrator access is required to capture and forward LAN packets.");
        }

        if (!NativeLibrary.TryLoad("wpcap.dll", out var libraryHandle))
        {
            throw new InvalidOperationException(
                "WinPcap or Npcap is not installed. Install Npcap in WinPcap-compatible mode, then restart LANtern Control.");
        }

        NativeLibrary.Free(libraryHandle);

        foreach (var serviceName in new[] { "npcap", "npf" })
        {
            if (await IsServicePresentAsync(serviceName, cancellationToken))
            {
                await StartServiceAsync(serviceName, cancellationToken);
                return;
            }
        }

        throw new InvalidOperationException(
            "The packet capture driver is installed but its Windows service was not found.");
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<bool> IsServicePresentAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var result = await RunScAsync($"query {serviceName}", cancellationToken);
        return result.ExitCode == 0;
    }

    private static async Task StartServiceAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var query = await RunScAsync($"query {serviceName}", cancellationToken);
        if (query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var start = await RunScAsync($"start {serviceName}", cancellationToken);
        if (start.ExitCode != 0 &&
            !start.Output.Contains("1056", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Windows could not start the {serviceName} packet capture driver.");
        }
    }

    private static async Task<ProcessResult> RunScAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "sc.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new InvalidOperationException("Windows Service Control could not be started.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(
            process.ExitCode,
            $"{await standardOutput}{await standardError}");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
