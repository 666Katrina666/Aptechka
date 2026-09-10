namespace Aptechka.Application.Sync;

public interface IDataSnapshotStore
{
    Task<DataSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);

    Task ReplaceAsync(DataSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<bool> TryReplaceAsync(
        DataSnapshot expected,
        DataSnapshot replacement,
        CancellationToken cancellationToken = default);
}
