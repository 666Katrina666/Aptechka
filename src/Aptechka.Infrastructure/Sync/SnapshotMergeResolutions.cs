using Aptechka.Application.Sync;

namespace Aptechka.Infrastructure.Sync;

internal sealed class SnapshotMergeResolutions
{
    private readonly IReadOnlyDictionary<string, SyncConflictResolution> byKey;

    private SnapshotMergeResolutions(IReadOnlyDictionary<string, SyncConflictResolution> byKey)
    {
        this.byKey = byKey;
    }

    public static SnapshotMergeResolutions None { get; } = new(
        new Dictionary<string, SyncConflictResolution>(StringComparer.Ordinal));

    public static SnapshotMergeResolutions Create(IReadOnlyList<SyncConflictResolution>? resolutions)
    {
        if (resolutions is null || resolutions.Count == 0)
        {
            return None;
        }

        var byKey = new Dictionary<string, SyncConflictResolution>(
            resolutions.Count,
            StringComparer.Ordinal);
        foreach (var resolution in resolutions)
        {
            ArgumentNullException.ThrowIfNull(resolution);
            ArgumentNullException.ThrowIfNull(resolution.Conflict);
            if (!Enum.IsDefined(resolution.Side))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolutions),
                    "Сторона разрешения конфликта неизвестна.");
            }

            if (byKey.TryGetValue(resolution.Conflict.Key, out var existing) &&
                existing.Side != resolution.Side)
            {
                throw new ArgumentException(
                    "Для одного конфликта нельзя выбрать обе стороны.",
                    nameof(resolutions));
            }

            byKey[resolution.Conflict.Key] = resolution;
        }

        return new SnapshotMergeResolutions(byKey);
    }

    public bool TryResolve(SyncConflict current, out SyncConflictSide side)
    {
        ArgumentNullException.ThrowIfNull(current);
        side = default;
        if (current.Path.Equals(SnapshotMergeIdentities.ManifestPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (!byKey.TryGetValue(current.Key, out var resolution) ||
            !Matches(resolution.Conflict, current))
        {
            return false;
        }

        side = resolution.Side;
        return true;
    }

    public static byte[]? Choose(SyncConflictSide side, byte[]? local, byte[]? remote) =>
        side switch
        {
            SyncConflictSide.Local => local,
            SyncConflictSide.Remote => remote,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };

    public static T Choose<T>(SyncConflictSide side, T local, T remote) =>
        side switch
        {
            SyncConflictSide.Local => local,
            SyncConflictSide.Remote => remote,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };

    private static bool Matches(SyncConflict seen, SyncConflict current) =>
        string.Equals(seen.Key, current.Key, StringComparison.Ordinal) &&
        string.Equals(seen.Path, current.Path, StringComparison.Ordinal) &&
        string.Equals(seen.Field, current.Field, StringComparison.Ordinal) &&
        seen.Kind == current.Kind &&
        string.Equals(seen.BaseValueJson, current.BaseValueJson, StringComparison.Ordinal) &&
        string.Equals(seen.LocalValueJson, current.LocalValueJson, StringComparison.Ordinal) &&
        string.Equals(seen.RemoteValueJson, current.RemoteValueJson, StringComparison.Ordinal);
}
