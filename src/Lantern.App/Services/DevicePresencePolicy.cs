namespace Lantern.App.Services;

public enum DevicePresence
{
    Online,
    Offline,
    Hidden,
}

public static class DevicePresencePolicy
{
    public static TimeSpan OfflineAfter { get; } = TimeSpan.FromSeconds(15);

    public static TimeSpan HideAfter { get; } = TimeSpan.FromSeconds(45);

    public static DevicePresence Classify(DateTimeOffset lastSeen, DateTimeOffset now)
    {
        var age = now - lastSeen;
        if (age <= OfflineAfter)
        {
            return DevicePresence.Online;
        }

        return age < HideAfter ? DevicePresence.Offline : DevicePresence.Hidden;
    }
}
