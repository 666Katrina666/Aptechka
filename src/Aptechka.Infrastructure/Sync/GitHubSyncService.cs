using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Infrastructure.Sync;

public sealed class GitHubSyncService(
    IDataSnapshotStore snapshotStore,
    SyncStateStore stateStore,
    IGitHubDataClient gitHubClient,
    IClock clock) : ISyncService
{
    private const int MaxRemotePushAttempts = 3;
    private const int MaxLocalCasAttempts = 3;

    private const string DatasetMismatchMessage =
        "Локальная аптечка и GitHub имеют разные datasetId. Автоматическая замена запрещена.";
    private const string ConcurrentChangeMessage =
        "Локальные и удалённые данные изменились одновременно. Синхронизация остановлена без потери данных.";
    private const string LocalBusyMessage =
        "Локальные данные изменялись во время синхронизации. Повтори попытку.";
    private const string RepeatedHeadRaceMessage =
        "Удалённая ветка изменялась повторно. Синхронизация остановлена без потери локальных данных.";

    private readonly SnapshotMergeEngine mergeEngine = new();

    public async Task<SyncResult> SyncAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        target.EnsureValid();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("GitHub-токен не задан.", nameof(accessToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var safeDeviceName = NormalizeDeviceName(deviceName);
        var local = await snapshotStore.ReadAsync(cancellationToken);
        var remote = await gitHubClient.GetSnapshotAsync(target, accessToken, cancellationToken);

        if (remote is null)
        {
            return await InitializeRepositoryAsync(
                target,
                accessToken,
                safeDeviceName,
                local,
                cancellationToken);
        }

        return await SyncExistingAsync(
            target,
            accessToken,
            safeDeviceName,
            local,
            remote,
            cancellationToken);
    }

    private async Task<SyncResult> InitializeRepositoryAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        DataSnapshot local,
        CancellationToken cancellationToken)
    {
        if (local.IsEmpty)
        {
            await snapshotStore.EnsureInitializedAsync(cancellationToken);
            local = await snapshotStore.ReadAsync(cancellationToken);
        }

        var initializedManifest = ReadManifest(local);
        await gitHubClient.InitializeRepositoryAsync(
            target,
            accessToken,
            local.Files["aptechka.json"],
            deviceName,
            cancellationToken);

        var remote = await gitHubClient.GetSnapshotAsync(target, accessToken, cancellationToken)
            ?? throw new GitHubApiException(409, "GitHub не создал ветку после инициализации.");
        local = await snapshotStore.ReadAsync(cancellationToken);

        if (local.HasSameFiles(remote.Data))
        {
            await stateStore.SaveAsync(
                initializedManifest.DatasetId,
                remote.CommitSha,
                local,
                cancellationToken);
            return new SyncResult(
                SyncOutcome.Initialized,
                "Репозиторий данных создан.",
                remote.CommitSha);
        }

        try
        {
            var commitSha = await gitHubClient.CommitSnapshotAsync(
                target,
                accessToken,
                remote,
                local,
                deviceName,
                cancellationToken);
            await stateStore.SaveAsync(
                initializedManifest.DatasetId,
                commitSha,
                local,
                cancellationToken);
            return new SyncResult(
                SyncOutcome.Initialized,
                "Репозиторий данных создан и локальная позиция отправлена.",
                commitSha);
        }
        catch (GitHubHeadChangedException)
        {
            return await SyncExistingAsync(
                target,
                accessToken,
                deviceName,
                null,
                null,
                cancellationToken,
                consumedRemotePushAttempts: 1);
        }
    }

    private async Task<SyncResult> SyncExistingAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        DataSnapshot? primedLocal,
        GitHubRemoteSnapshot? primedRemote,
        CancellationToken cancellationToken,
        int consumedRemotePushAttempts = 0)
    {
        var remotePushAttempts = consumedRemotePushAttempts;
        var usePrimed = primedRemote is not null;

        while (remotePushAttempts < MaxRemotePushAttempts)
        {
            var retryBecauseOfHeadRace = false;
            for (var casAttempt = 0; casAttempt < MaxLocalCasAttempts; casAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var local = usePrimed && primedLocal is not null
                    ? primedLocal
                    : await snapshotStore.ReadAsync(cancellationToken);
                var remote = usePrimed
                    ? primedRemote!
                    : await gitHubClient.GetSnapshotAsync(target, accessToken, cancellationToken)
                        ?? throw new GitHubApiException(404, "Репозиторий или ветка не найдены.");
                usePrimed = false;

                var attempt = await TryOnceAsync(
                    target,
                    accessToken,
                    deviceName,
                    local,
                    remote,
                    cancellationToken);
                switch (attempt.Kind)
                {
                    case AttemptKind.Completed:
                        return attempt.Result!;
                    case AttemptKind.HeadRace:
                        remotePushAttempts++;
                        retryBecauseOfHeadRace = true;
                        break;
                    case AttemptKind.CasMiss:
                        continue;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(attempt.Kind), attempt.Kind, null);
                }

                if (retryBecauseOfHeadRace)
                {
                    break;
                }
            }

            if (retryBecauseOfHeadRace)
            {
                continue;
            }

            throw new InvalidOperationException(LocalBusyMessage);
        }

        throw new GitHubHeadChangedException(RepeatedHeadRaceMessage);
    }

    private async Task<AttemptOutcome> TryOnceAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        DataSnapshot local,
        GitHubRemoteSnapshot remote,
        CancellationToken cancellationToken)
    {
        if (local.IsEmpty)
        {
            if (!await snapshotStore.TryReplaceAsync(local, remote.Data, cancellationToken))
            {
                return AttemptOutcome.CasMiss();
            }

            var pulledManifest = ReadManifest(remote.Data);
            await stateStore.SaveAsync(
                pulledManifest.DatasetId,
                remote.CommitSha,
                remote.Data,
                cancellationToken);
            return AttemptOutcome.Completed(new SyncResult(
                SyncOutcome.Pulled,
                "Данные загружены с GitHub на это устройство.",
                remote.CommitSha));
        }

        var localManifest = ReadManifest(local);
        var remoteManifest = ReadManifest(remote.Data);
        if (!string.Equals(localManifest.DatasetId, remoteManifest.DatasetId, StringComparison.Ordinal))
        {
            return AttemptOutcome.Completed(new SyncResult(
                SyncOutcome.Conflict,
                DatasetMismatchMessage));
        }

        var state = await stateStore.LoadAsync(cancellationToken);
        var baseSnapshot = state is not null &&
                           string.Equals(state.DatasetId, localManifest.DatasetId, StringComparison.Ordinal)
            ? state.GetBaseSnapshot()
            : null;

        if (baseSnapshot is null)
        {
            return await TryWithoutBaseAsync(
                target,
                accessToken,
                deviceName,
                local,
                remote,
                localManifest,
                remoteManifest,
                cancellationToken);
        }

        var merge = mergeEngine.Merge(baseSnapshot, local, remote.Data, clock.UtcNow);
        if (merge.HasConflicts)
        {
            return AttemptOutcome.Completed(new SyncResult(
                SyncOutcome.Conflict,
                ConcurrentChangeMessage,
                conflicts: merge.Conflicts));
        }

        var merged = merge.MergedSnapshot
            ?? throw new InvalidDataException("Объединение не вернуло снимок.");

        if (merged.HasSameFiles(remote.Data))
        {
            if (!local.HasSameFiles(merged) &&
                !await snapshotStore.TryReplaceAsync(local, merged, cancellationToken))
            {
                return AttemptOutcome.CasMiss();
            }

            await stateStore.SaveAsync(
                localManifest.DatasetId,
                remote.CommitSha,
                merged,
                cancellationToken);
            return AttemptOutcome.Completed(new SyncResult(
                local.HasSameFiles(remote.Data) ? SyncOutcome.UpToDate : SyncOutcome.Pulled,
                local.HasSameFiles(remote.Data)
                    ? "Локальные данные и GitHub уже совпадают."
                    : "Изменения с GitHub загружены на устройство.",
                remote.CommitSha));
        }

        if (!local.HasSameFiles(merged) &&
            !await snapshotStore.TryReplaceAsync(local, merged, cancellationToken))
        {
            return AttemptOutcome.CasMiss();
        }

        try
        {
            var commitSha = await gitHubClient.CommitSnapshotAsync(
                target,
                accessToken,
                remote,
                merged,
                deviceName,
                cancellationToken);
            await stateStore.SaveAsync(
                localManifest.DatasetId,
                commitSha,
                merged,
                cancellationToken);
            return AttemptOutcome.Completed(new SyncResult(
                SyncOutcome.Pushed,
                "Локальные изменения отправлены в GitHub.",
                commitSha));
        }
        catch (GitHubHeadChangedException)
        {
            return AttemptOutcome.HeadRace();
        }
    }

    private async Task<AttemptOutcome> TryWithoutBaseAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        DataSnapshot local,
        GitHubRemoteSnapshot remote,
        DatasetManifest localManifest,
        DatasetManifest remoteManifest,
        CancellationToken cancellationToken)
    {
        var action = SnapshotSyncPlanner.Plan(null, local, remote.Data);
        switch (action)
        {
            case SnapshotSyncAction.Push:
                try
                {
                    var commitSha = await gitHubClient.CommitSnapshotAsync(
                        target,
                        accessToken,
                        remote,
                        local,
                        deviceName,
                        cancellationToken);
                    await stateStore.SaveAsync(
                        localManifest.DatasetId,
                        commitSha,
                        local,
                        cancellationToken);
                    return AttemptOutcome.Completed(new SyncResult(
                        SyncOutcome.Pushed,
                        "Локальные изменения отправлены в GitHub.",
                        commitSha));
                }
                catch (GitHubHeadChangedException)
                {
                    return AttemptOutcome.HeadRace();
                }

            case SnapshotSyncAction.Pull:
                if (!await snapshotStore.TryReplaceAsync(local, remote.Data, cancellationToken))
                {
                    return AttemptOutcome.CasMiss();
                }

                await stateStore.SaveAsync(
                    remoteManifest.DatasetId,
                    remote.CommitSha,
                    remote.Data,
                    cancellationToken);
                return AttemptOutcome.Completed(new SyncResult(
                    SyncOutcome.Pulled,
                    "Изменения с GitHub загружены на устройство.",
                    remote.CommitSha));

            case SnapshotSyncAction.UpToDate:
                await stateStore.SaveAsync(
                    localManifest.DatasetId,
                    remote.CommitSha,
                    remote.Data,
                    cancellationToken);
                return AttemptOutcome.Completed(new SyncResult(
                    SyncOutcome.UpToDate,
                    "Локальные данные и GitHub уже совпадают.",
                    remote.CommitSha));

            case SnapshotSyncAction.Conflict:
                return AttemptOutcome.Completed(new SyncResult(
                    SyncOutcome.Conflict,
                    ConcurrentChangeMessage));

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static DatasetManifest ReadManifest(DataSnapshot snapshot)
    {
        if (!snapshot.Files.TryGetValue("aptechka.json", out var content))
        {
            throw new InvalidDataException("В снимке отсутствует aptechka.json.");
        }

        var manifest = JsonSerializer.Deserialize<DatasetManifest>(content, AptechkaJson.Options)
            ?? throw new InvalidDataException("Манифест аптечки пуст.");
        manifest.EnsureCompatible();
        return manifest;
    }

    private static string NormalizeDeviceName(string value)
    {
        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length switch
        {
            0 => "unknown-device",
            > 40 => normalized[..40],
            _ => normalized,
        };
    }

    private enum AttemptKind
    {
        Completed,
        CasMiss,
        HeadRace,
    }

    private readonly record struct AttemptOutcome(AttemptKind Kind, SyncResult? Result)
    {
        public static AttemptOutcome Completed(SyncResult result) => new(AttemptKind.Completed, result);

        public static AttemptOutcome CasMiss() => new(AttemptKind.CasMiss, null);

        public static AttemptOutcome HeadRace() => new(AttemptKind.HeadRace, null);
    }
}
