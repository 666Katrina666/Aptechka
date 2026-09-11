using Aptechka.Application.Sync;

namespace Aptechka.Infrastructure.Sync;

public sealed class SyncStateInspector(
    IDataSnapshotStore snapshotStore,
    SyncStateStore stateStore) : ISyncStateInspector
{
    public async Task<SyncInspection> InspectAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        if (state is null)
        {
            return new SyncInspection(SyncInspectionCondition.NoSuccessfulSync, null, null);
        }

        DataSnapshot local;
        DataSnapshot @base;
        try
        {
            local = await snapshotStore.ReadAsync(cancellationToken);
            @base = state.GetBaseSnapshot();
        }
        catch (FormatException exception)
        {
            throw new SyncFailureException(
                SyncFailureKind.InvalidData,
                "Данные имеют повреждённый или несовместимый формат.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SyncFailureException(
                SyncFailureKind.LocalStorage,
                "Не удалось прочитать или сохранить локальные данные.",
                exception);
        }

        return new SyncInspection(
            local.HasSameFiles(@base)
                ? SyncInspectionCondition.MatchesBase
                : SyncInspectionCondition.LocalChanges,
            state.LastSuccessfulAt,
            state.LastSuccessfulOutcome);
    }
}
