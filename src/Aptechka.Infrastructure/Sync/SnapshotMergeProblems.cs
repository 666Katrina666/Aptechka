using Aptechka.Domain.Inventory;

namespace Aptechka.Infrastructure.Sync;

internal static class SnapshotMergeProblems
{
    public const string PathPrefix = "problems/";

    public static bool UsersEqual(Problem left, Problem right) =>
        left.Name == right.Name &&
        left.Note == right.Note &&
        (left.DeletedAt is not null) == (right.DeletedAt is not null) &&
        SetEquals(left.Aliases, right.Aliases, StringComparer.OrdinalIgnoreCase) &&
        SetEquals(left.ItemIds, right.ItemIds, StringComparer.Ordinal);

    public static IReadOnlyList<string> MergeItemIds(
        IReadOnlyList<string> @base,
        IReadOnlyList<string> local,
        IReadOnlyList<string> remote)
    {
        var baseItems = Unique(@base, StringComparer.Ordinal);
        var localItems = Unique(local, StringComparer.Ordinal);
        var remoteItems = Unique(remote, StringComparer.Ordinal);
        var baseKeys = new HashSet<string>(baseItems, StringComparer.Ordinal);
        var localKeys = new HashSet<string>(localItems, StringComparer.Ordinal);
        var remoteKeys = new HashSet<string>(remoteItems, StringComparer.Ordinal);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in baseItems)
        {
            if (localKeys.Contains(value) && remoteKeys.Contains(value) && seen.Add(value))
            {
                result.Add(value);
            }
        }

        foreach (var value in localItems)
        {
            if (!baseKeys.Contains(value) && seen.Add(value))
            {
                result.Add(value);
            }
        }

        foreach (var value in remoteItems)
        {
            if (!baseKeys.Contains(value) && seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    public static Problem WithMergedMetadata(
        Problem chosen,
        Problem @base,
        Problem local,
        Problem remote,
        DateTimeOffset mergedAt,
        DateTimeOffset? deletedAt) =>
        chosen with
        {
            CreatedAt = Earlier(@base.CreatedAt, local.CreatedAt, remote.CreatedAt),
            UpdatedAt = mergedAt,
            Revision = Math.Max(local.Revision, remote.Revision) + 1,
            DeletedAt = deletedAt,
        };

    private static bool SetEquals(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        StringComparer comparer)
    {
        var leftKeys = new HashSet<string>(Unique(left, comparer), comparer);
        return leftKeys.SetEquals(Unique(right, comparer));
    }

    private static IReadOnlyList<string> Unique(IEnumerable<string> values, StringComparer comparer)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(comparer);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second, DateTimeOffset third)
    {
        var earliest = first <= second ? first : second;
        return earliest <= third ? earliest : third;
    }
}
