using Aptechka.Application.Sync;

namespace Aptechka.Infrastructure.Sync;

public enum SnapshotSyncAction
{
    Push,
    Pull,
    UpToDate,
    Conflict,
}

public static class SnapshotSyncPlanner
{
    public static SnapshotSyncAction Plan(
        DataSnapshot? baseSnapshot,
        DataSnapshot local,
        DataSnapshot remote)
    {
        if (AreEqual(local, remote))
        {
            return SnapshotSyncAction.UpToDate;
        }

        if (baseSnapshot is null)
        {
            if (IsCompatibleSubset(remote, local))
            {
                return SnapshotSyncAction.Push;
            }

            if (IsCompatibleSubset(local, remote))
            {
                return SnapshotSyncAction.Pull;
            }

            return SnapshotSyncAction.Conflict;
        }

        var localChanged = !AreEqual(local, baseSnapshot);
        var remoteChanged = !AreEqual(remote, baseSnapshot);

        return (localChanged, remoteChanged) switch
        {
            (false, false) => SnapshotSyncAction.UpToDate,
            (true, false) => SnapshotSyncAction.Push,
            (false, true) => SnapshotSyncAction.Pull,
            _ => SnapshotSyncAction.Conflict,
        };
    }

    public static bool AreEqual(DataSnapshot left, DataSnapshot right) =>
        left.HasSameFiles(right);

    private static bool IsCompatibleSubset(DataSnapshot subset, DataSnapshot superset) =>
        subset.Files.All(pair =>
            superset.Files.TryGetValue(pair.Key, out var other) &&
            pair.Value.AsSpan().SequenceEqual(other));
}
