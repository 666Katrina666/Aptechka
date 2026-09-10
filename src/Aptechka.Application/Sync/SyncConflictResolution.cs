namespace Aptechka.Application.Sync;

public enum SyncConflictSide
{
    Local,
    Remote,
}

public sealed record SyncConflictResolution(
    SyncConflict Conflict,
    SyncConflictSide Side);
