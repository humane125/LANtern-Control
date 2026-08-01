using Lantern.App.ViewModels;
using Xunit;

namespace Lantern.App.Tests;

public sealed class TrafficSamplingProfileTests
{
    [Fact]
    public void LiveChart_SamplesEverySecondAndKeepsOneHour()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), TrafficSamplingProfile.Interval);
        Assert.Equal(TimeSpan.FromHours(1), TrafficSamplingProfile.Retention);
        Assert.Equal(3_600, TrafficSamplingProfile.Capacity);
    }
}
