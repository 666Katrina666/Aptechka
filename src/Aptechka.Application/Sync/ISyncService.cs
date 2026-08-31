namespace Aptechka.Application.Sync;

public interface ISyncService
{
    Task<SyncResult> SyncAsync(
        SyncTarget target,
        string accessToken,
        string deviceName,
        CancellationToken cancellationToken = default);
}
