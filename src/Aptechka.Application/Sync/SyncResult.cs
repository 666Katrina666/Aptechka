namespace Aptechka.Application.Sync;

public enum SyncOutcome
{
    Initialized,
    Pushed,
    Pulled,
    UpToDate,
    Conflict,
}

public sealed record SyncResult(
    SyncOutcome Outcome,
    string Message,
    string? CommitSha = null);
