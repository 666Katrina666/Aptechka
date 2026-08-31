using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Infrastructure.Sync;

public sealed class GitHubSyncService(
    IDataSnapshotStore snapshotStore,
    SyncStateStore stateStore,
    GitHubDataClient gitHubClient) : ISyncService
{
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

        var safeDeviceName = NormalizeDeviceName(deviceName);
        var local = await snapshotStore.ReadAsync(cancellationToken);
        var remote = await gitHubClient.GetSnapshotAsync(target, accessToken, cancellationToken);

        if (remote is null)
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
                safeDeviceName,
                cancellationToken);

            remote = await gitHubClient.GetSnapshotAsync(target, accessToken, cancellationToken)
                ?? throw new GitHubApiException(409, "GitHub не создал ветку после инициализации.");

            if (!SnapshotSyncPlanner.AreEqual(local, remote.Data))
            {
                var commitSha = await gitHubClient.CommitSnapshotAsync(
                    target,
                    accessToken,
                    remote,
                    local,
                    safeDeviceName,
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

        var remoteManifest = ReadManifest(remote.Data);
        if (local.IsEmpty)
        {
            await snapshotStore.ReplaceAsync(remote.Data, cancellationToken);
            await stateStore.SaveAsync(
                remoteManifest.DatasetId,
                remote.CommitSha,
                remote.Data,
                cancellationToken);
            return new SyncResult(
                SyncOutcome.Pulled,
                "Данные загружены с GitHub на это устройство.",
                remote.CommitSha);
        }

        var localManifest = ReadManifest(local);
        if (!string.Equals(
                localManifest.DatasetId,
                remoteManifest.DatasetId,
                StringComparison.Ordinal))
        {
            return new SyncResult(
                SyncOutcome.Conflict,
                "Локальная аптечка и GitHub имеют разные datasetId. Автоматическая замена запрещена.");
        }

        var state = await stateStore.LoadAsync(cancellationToken);
        var baseSnapshot = state is not null &&
                           string.Equals(state.DatasetId, localManifest.DatasetId, StringComparison.Ordinal)
            ? state.GetBaseSnapshot()
            : null;

        var action = SnapshotSyncPlanner.Plan(baseSnapshot, local, remote.Data);
        switch (action)
        {
            case SnapshotSyncAction.Push:
                {
                    var commitSha = await gitHubClient.CommitSnapshotAsync(
                        target,
                        accessToken,
                        remote,
                        local,
                        safeDeviceName,
                        cancellationToken);
                    await stateStore.SaveAsync(
                        localManifest.DatasetId,
                        commitSha,
                        local,
                        cancellationToken);
                    return new SyncResult(
                        SyncOutcome.Pushed,
                        "Локальные изменения отправлены в GitHub.",
                        commitSha);
                }

            case SnapshotSyncAction.Pull:
                await snapshotStore.ReplaceAsync(remote.Data, cancellationToken);
                await stateStore.SaveAsync(
                    remoteManifest.DatasetId,
                    remote.CommitSha,
                    remote.Data,
                    cancellationToken);
                return new SyncResult(
                    SyncOutcome.Pulled,
                    "Изменения с GitHub загружены на устройство.",
                    remote.CommitSha);

            case SnapshotSyncAction.UpToDate:
                await stateStore.SaveAsync(
                    localManifest.DatasetId,
                    remote.CommitSha,
                    remote.Data,
                    cancellationToken);
                return new SyncResult(
                    SyncOutcome.UpToDate,
                    "Локальные данные и GitHub уже совпадают.",
                    remote.CommitSha);

            case SnapshotSyncAction.Conflict:
                return new SyncResult(
                    SyncOutcome.Conflict,
                    "Локальные и удалённые данные изменились одновременно. P1 остановил синхронизацию без потери данных.");

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
}
