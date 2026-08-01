using System.Diagnostics;
using System.IO;
using System.Net;
using Lantern.Core.Networking;

namespace Lantern.App.Services;

public static class WindowsNeighborCache
{
    public static async Task<IReadOnlyList<NeighborCacheEntry>> ReadAsync(
        AdapterProfile profile,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "arp.exe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("-N");
        startInfo.ArgumentList.Add(profile.LocalAddress.ToString());

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Windows could not read its neighbor cache.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Windows could not read its neighbor cache."
                    : error.Trim());
        }

        return NeighborCacheParser.Parse(
            output,
            profile.LocalAddress,
            profile.PrefixLength);
    }

    public static async Task<bool> DeleteAsync(
        AdapterProfile profile,
        IPAddress address,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "arp.exe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(address.ToString());
        startInfo.ArgumentList.Add(profile.LocalAddress.ToString());

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Windows could not repair its neighbor cache.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        _ = await outputTask;
        _ = await errorTask;
        return process.ExitCode == 0;
    }
}
