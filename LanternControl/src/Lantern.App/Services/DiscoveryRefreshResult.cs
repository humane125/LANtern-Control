namespace Lantern.App.Services;

public sealed record DiscoveryRefreshResult(
    int ProbesSent,
    int RepliesReceived,
    int KnownDeviceCount)
{
    public string StatusMessage =>
        $"ARP sweep complete — {ProbesSent} probes, {RepliesReceived} " +
        $"{(RepliesReceived == 1 ? "reply" : "replies")}, {KnownDeviceCount} known devices.";
}
