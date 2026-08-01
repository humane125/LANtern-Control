using Lantern.App.Controls;
using Xunit;

namespace Lantern.App.Tests;

public sealed class ResponsiveUniformGridTests
{
    [Theory]
    [InlineData(800, 900, 4, 2)]
    [InlineData(900, 900, 4, 4)]
    [InlineData(1_200, 900, 3, 3)]
    public void GetColumnCount_UsesTwoColumnsBelowBreakpoint(
        double width,
        double breakpoint,
        int wideColumns,
        int expected)
    {
        Assert.Equal(
            expected,
            ResponsiveUniformGrid.GetColumnCount(width, breakpoint, wideColumns));
    }

    [Fact]
    public void GetColumnCount_RejectsNonPositiveWideColumnCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ResponsiveUniformGrid.GetColumnCount(800, 900, 0));
    }
}
