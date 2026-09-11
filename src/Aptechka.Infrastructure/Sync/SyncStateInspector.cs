using System.Text.Json;
using Aptechka.Application.Sync;

namespace Aptechka.Infrastructure.Sync;

public sealed class SyncStateInspector(
    IDataSnapshotStore snapshotStore,
    SyncStateStore stateStore) : ISyncStateInspector
{
    public async Task<SyncInspection> InspectAsync(CancellationToken cancellationToken = default)
    {
        SyncState? state;
        try
        {
            state = await stateStore.LoadAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw Classify(exception, cancellationToken);
        }

        if (state is null)
        {
            return new SyncInspection(SyncInspectionCondition.NoSuccessfulSync, null, null);
        }

        try
        {
            var local = await snapshotStore.ReadAsync(cancellationToken);
            var @base = state.GetBaseSnapshot();
            return new SyncInspection(
                local.HasSameFiles(@base)
                    ? SyncInspectionCondition.MatchesBase
                    : SyncInspectionCondition.LocalChanges,
                state.LastSuccessfulAt,
                state.LastSuccessfulOutcome);
        }
        catch (Exception exception)
        {
            throw Classify(exception, cancellationToken);
        }
    }

    private static Exception Classify(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return exception;
        }

        return exception switch
        {
            JsonException or InvalidDataException or FormatException => new SyncFailureException(
                SyncFailureKind.InvalidData,
                "Данные имеют повреждённый или несовместимый формат.",
                exception),
            IOException or UnauthorizedAccessException => new SyncFailureException(
                SyncFailureKind.LocalStorage,
                "Не удалось прочитать или сохранить локальные данные.",
                exception),
            _ => exception,
        };
    }
}
