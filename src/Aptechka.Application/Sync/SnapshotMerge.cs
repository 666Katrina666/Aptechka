namespace Aptechka.Application.Sync;

public enum SyncConflictKind
{
    FieldChangedBoth,
    DeleteVsModify,
    FileChangedBoth,
    FileDeleteVsModify,
}

public sealed record SyncConflict(
    string Key,
    string Path,
    string Field,
    SyncConflictKind Kind,
    string? BaseValueJson,
    string? LocalValueJson,
    string? RemoteValueJson);

public sealed record SnapshotMergeResult(
    DataSnapshot? MergedSnapshot,
    IReadOnlyList<SyncConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}
