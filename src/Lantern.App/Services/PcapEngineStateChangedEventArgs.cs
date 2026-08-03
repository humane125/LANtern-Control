namespace Lantern.App.Services;

public sealed class PcapEngineStateChangedEventArgs(
    bool isRunning,
    string statusMessage,
    string? failureMessage = null) : EventArgs
{
    public bool IsRunning { get; } = isRunning;
    public string StatusMessage { get; } = statusMessage;
    public string? FailureMessage { get; } = failureMessage;
}
