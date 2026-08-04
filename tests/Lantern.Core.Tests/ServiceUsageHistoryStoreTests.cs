using Lantern.Core.Services;
using Lantern.Core.Settings;

namespace Lantern.Core.Tests;

public sealed class ServiceUsageHistoryStoreTests
{
    private static readonly DateOnly Today = new(2026, 8, 4);

    [Fact]
    public async Task MergeAndSaveAsync_PersistsDailyTotalsAcrossStoreInstances()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var first = new ServiceUsageHistoryStore(directory, () => Today);
            var second = new ServiceUsageHistoryStore(directory, () => Today);

            await first.MergeAndSaveAsync([Completed("youtube", 1_000, 200)]);
            await second.MergeAndSaveAsync([Completed("youtube", 500, 50)]);
            var loaded = await new ServiceUsageHistoryStore(directory, () => Today).LoadAsync();

            var aggregate = Assert.Single(loaded.Days).Services.Single();
            Assert.Equal("E261190DBD54", aggregate.MacKey);
            Assert.Equal("youtube", aggregate.ServiceId);
            Assert.Equal(1_500, aggregate.DownloadBytes);
            Assert.Equal(250, aggregate.UploadBytes);
            Assert.Equal(2, aggregate.SessionCount);
            Assert.Equal(TimeSpan.FromSeconds(20), aggregate.ActiveDuration);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MergeAndSaveAsync_SeparatesDevicesAndServices()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ServiceUsageHistoryStore(directory, () => Today);

            var history = await store.MergeAndSaveAsync(
            [
                Completed("youtube", 100, 10),
                Completed("discord", 200, 20),
                Completed("youtube", 300, 30) with { MacKey = "0E4F69CCE4F0" },
            ]);

            Assert.Equal(3, Assert.Single(history.Days).Services.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MergeAndSaveAsync_KeepsOnlyMostRecentThirtyDays()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ServiceUsageHistoryStore(directory, () => Today);
            for (var daysAgo = 0; daysAgo < 35; daysAgo++)
            {
                var end = Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Local);
                await store.MergeAndSaveAsync(
                    [Completed("youtube", 1, 0) with
                    {
                        StartedAt = new DateTimeOffset(end.AddSeconds(-10)),
                        EndedAt = new DateTimeOffset(end),
                    }]);
            }

            var loaded = await store.LoadAsync();

            Assert.Equal(30, loaded.Days.Count);
            Assert.Equal(Today, loaded.Days.Max(day => day.Date));
            Assert.Equal(Today.AddDays(-29), loaded.Days.Min(day => day.Date));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_MalformedPrimaryUsesLastBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ServiceUsageHistoryStore(directory, () => Today);
            await store.MergeAndSaveAsync([Completed("youtube", 100, 10)]);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "service-history.json"),
                "{not-json");

            var loaded = await store.LoadAsync();

            Assert.Equal(100, Assert.Single(Assert.Single(loaded.Days).Services).DownloadBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CompletedServiceSession Completed(
        string serviceId,
        long download,
        long upload)
    {
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        return new CompletedServiceSession(
            "E261190DBD54",
            serviceId,
            serviceId == "youtube" ? "YouTube" : "Discord",
            start,
            start.AddSeconds(10),
            download,
            upload,
            1);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lantern-service-history-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
