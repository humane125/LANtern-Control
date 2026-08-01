using Lantern.App.Services;
using Xunit;

namespace Lantern.App.Tests;

public sealed class DeviceListRefreshPolicyTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void ShouldRefresh_OnlyWhenNoEditTransactionIsActive(
        bool textInputFocused,
        bool addingNew,
        bool editingItem,
        bool expected)
    {
        Assert.Equal(
            expected,
            DeviceListRefreshPolicy.ShouldRefresh(textInputFocused, addingNew, editingItem));
    }
}
