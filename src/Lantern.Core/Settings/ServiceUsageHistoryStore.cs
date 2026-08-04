using System.Collections.Concurrent;
using System.Text.Json;
using Lantern.Core.Control;
using Lantern.Core.Services;

namespace Lantern.Core.Settings;

public sealed class ServiceUsageHistoryStore
{
    private const int RetainedDays = 30;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string directory;
    private readonly string historyPath;
    private readonly string backupPath;
    private readonly Func<DateOnly> localToday;

    public ServiceUsageHistoryStore(
        string? directory = null,
        Func<DateOnly>? localToday = null)
    {
        this.directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LANternControl");
        historyPath = Path.Combine(this.directory, "service-history.json");
        backupPath = Path.Combine(this.directory, "service-history.backup.json");
        this.localToday = localToday ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }

    public async Task<ServiceUsageHistory> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var pathLock = GetPathLock();
        await pathLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadWithoutLockAsync(cancellationToken);
        }
        finally
        {
            pathLock.Release();
        }
    }

    public async Task<ServiceUsageHistory> MergeAndSaveAsync(
        IEnumerable<CompletedServiceSession> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var pathLock = GetPathLock();
        await pathLock.WaitAsync(cancellationToken);
        try
        {
            var history = await LoadWithoutLockAsync(cancellationToken);
            foreach (var session in sessions)
            {
                Merge(history, session);
            }

            NormalizeAndTrim(history);
            await SaveWithoutLockAsync(history, cancellationToken);
            return history;
        }
        finally
        {
            pathLock.Release();
        }
    }

    private async Task<ServiceUsageHistory> LoadWithoutLockAsync(
        CancellationToken cancellationToken)
    {
        var history = await TryLoadAsync(historyPath, cancellationToken) ??
                      await TryLoadAsync(backupPath, cancellationToken) ??
                      new ServiceUsageHistory();
        NormalizeAndTrim(history);
        return history;
    }

    private static async Task<ServiceUsageHistory?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<ServiceUsageHistory>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveWithoutLockAsync(
        ServiceUsageHistory history,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"service-history-{Guid.NewGuid():N}.tmp");
        var backupTemporaryPath = Path.Combine(
            directory,
            $"service-history-backup-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    history,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Copy(temporaryPath, backupTemporaryPath);
            File.Move(backupTemporaryPath, backupPath, overwrite: true);
            File.Move(temporaryPath, historyPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(backupTemporaryPath);
        }
    }

    private static void Merge(
        ServiceUsageHistory history,
        CompletedServiceSession session)
    {
        var date = DateOnly.FromDateTime(session.EndedAt.LocalDateTime);
        var day = history.Days.FirstOrDefault(candidate => candidate.Date == date);
        if (day is null)
        {
            day = new ServiceUsageDay { Date = date };
            history.Days.Add(day);
        }

        var macKey = TrafficPolicy.NormalizeMac(session.MacKey);
        var aggregate = day.Services.FirstOrDefault(candidate =>
            candidate.MacKey.Equals(macKey, StringComparison.OrdinalIgnoreCase) &&
            candidate.ServiceId.Equals(session.ServiceId, StringComparison.OrdinalIgnoreCase));
        if (aggregate is null)
        {
            aggregate = new ServiceUsageAggregate
            {
                MacKey = macKey,
                ServiceId = session.ServiceId,
                ServiceName = session.ServiceName,
            };
            day.Services.Add(aggregate);
        }

        aggregate.ServiceName = session.ServiceName;
        aggregate.DownloadBytes += Math.Max(0, session.DownloadBytes);
        aggregate.UploadBytes += Math.Max(0, session.UploadBytes);
        aggregate.ActiveDuration += session.ActiveDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : session.ActiveDuration;
        aggregate.SessionCount++;
        if (session.EndedAt > aggregate.LastActivity)
        {
            aggregate.LastActivity = session.EndedAt;
        }
    }

    private void NormalizeAndTrim(ServiceUsageHistory history)
    {
        var firstRetainedDate = localToday().AddDays(-(RetainedDays - 1));
        history.Days.RemoveAll(day => day.Date < firstRetainedDate);
        history.Days.Sort((left, right) => right.Date.CompareTo(left.Date));
        foreach (var day in history.Days)
        {
            day.Services.Sort((left, right) =>
            {
                var macComparison = StringComparer.OrdinalIgnoreCase.Compare(
                    left.MacKey,
                    right.MacKey);
                return macComparison != 0
                    ? macComparison
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        left.ServiceName,
                        right.ServiceName);
            });
        }
    }

    private SemaphoreSlim GetPathLock() =>
        SaveLocks.GetOrAdd(historyPath, _ => new SemaphoreSlim(1, 1));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A later save uses a unique temporary path.
        }
    }
}
