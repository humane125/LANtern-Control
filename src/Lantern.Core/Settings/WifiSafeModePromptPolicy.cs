using Lantern.Core.Networking;

namespace Lantern.Core.Settings;

public static class WifiSafeModePromptPolicy
{
    public static bool ShouldPrompt(
        AdapterConnectionKind connectionKind,
        bool safeModeEnabled,
        bool suppressed,
        bool shownThisLaunch) =>
        connectionKind == AdapterConnectionKind.Wifi &&
        !safeModeEnabled &&
        !suppressed &&
        !shownThisLaunch;
}
