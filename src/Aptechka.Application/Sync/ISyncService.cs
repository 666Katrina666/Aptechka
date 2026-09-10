namespace Aptechka.Application.Sync;

public interface ISyncService
{
    Task<SyncResult> SyncAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        CancellationToken cancellationToken = default);

    Task<SyncResult> ResolveConflictsAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        IReadOnlyList<SyncConflictResolution> resolutions,
        CancellationToken cancellationToken = default);
}
