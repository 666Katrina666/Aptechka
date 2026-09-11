namespace Aptechka.Application.Sync;

public enum SyncInspectionCondition
{
    NoSuccessfulSync,
    MatchesBase,
    LocalChanges,
}

public sealed record SyncInspection(
    SyncInspectionCondition Condition,
    DateTimeOffset? LastSuccessfulAt,
    SyncOutcome? LastSuccessfulOutcome);

public interface ISyncStateInspector
{
    Task<SyncInspection> InspectAsync(CancellationToken cancellationToken = default);
}
