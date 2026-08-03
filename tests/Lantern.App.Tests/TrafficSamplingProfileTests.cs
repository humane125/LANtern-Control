using Lantern.App.ViewModels;
using Xunit;

namespace Lantern.App.Tests;

public sealed class TrafficSamplingProfileTests
{
    [Fact]
    public void LiveChart_SamplesEveryTwoAndHalfSecondsAndKeepsTenMinutes()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2500), TrafficSamplingProfile.Interval);
        Assert.Equal(TimeSpan.FromMinutes(10), TrafficSamplingProfile.Retention);
        Assert.Equal(240, TrafficSamplingProfile.Capacity);
    }
}
