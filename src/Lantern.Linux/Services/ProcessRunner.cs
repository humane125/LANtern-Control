using System.ComponentModel;
using System.Diagnostics;

namespace Lantern.Linux.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string ErrorSummary =>
        string.IsNullOrWhiteSpace(StandardError)
            ? $"exit code {ExitCode}"
            : StandardError.Trim();
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"The '{fileName}' process did not start.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"The '{fileName}' command could not be started.",
                exception);
        }

        using (process)
        {
            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}
