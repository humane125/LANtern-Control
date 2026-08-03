namespace Lantern.Linux.Services;

public sealed class LinuxEngineStateChangedEventArgs(
    bool isRunning,
    string statusMessage,
    string? failureMessage = null) : EventArgs
{
    public bool IsRunning { get; } = isRunning;
    public string StatusMessage { get; } = statusMessage;
    public string? FailureMessage { get; } = failureMessage;
}
