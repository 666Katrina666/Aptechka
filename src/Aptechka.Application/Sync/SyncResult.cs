namespace Aptechka.Application.Sync;

public enum SyncOutcome
{
    Initialized,
    Pushed,
    Pulled,
    UpToDate,
    Conflict,
}

public sealed record SyncResult
{
    public SyncResult(
        SyncOutcome outcome,
        string message,
        string? commitSha = null,
        IReadOnlyList<SyncConflict>? conflicts = null)
    {
        Outcome = outcome;
        Message = message;
        CommitSha = commitSha;
        Conflicts = conflicts ?? [];
    }

    public SyncOutcome Outcome { get; }

    public string Message { get; }

    public string? CommitSha { get; }

    public IReadOnlyList<SyncConflict> Conflicts { get; }
}
