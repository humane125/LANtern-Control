namespace Lantern.App.Services;

public static class DeviceListRefreshPolicy
{
    public static bool ShouldRefresh(
        bool textInputFocused,
        bool addingNew,
        bool editingItem) =>
        !textInputFocused && !addingNew && !editingItem;
}
