using Aptechka.Domain.Inventory;

namespace Aptechka.Infrastructure.Sync;

internal static class SnapshotMergeShopping
{
    public const string PathPrefix = "shopping/";

    public static bool UsersEqual(ShoppingItem left, ShoppingItem right) =>
        left.IsRequested == right.IsRequested &&
        left.Note == right.Note &&
        (left.DeletedAt is not null) == (right.DeletedAt is not null);

    public static bool SameUserContent(ShoppingItem left, ShoppingItem right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
        string.Equals(left.ItemId, right.ItemId, StringComparison.Ordinal) &&
        UsersEqual(left, right);

    public static ShoppingItem WithMergedMetadata(
        ShoppingItem chosen,
        ShoppingItem @base,
        ShoppingItem local,
        ShoppingItem remote,
        DateTimeOffset mergedAt,
        DateTimeOffset? deletedAt) =>
        chosen with
        {
            CreatedAt = Earlier(@base.CreatedAt, local.CreatedAt, remote.CreatedAt),
            UpdatedAt = mergedAt,
            Revision = Math.Max(local.Revision, remote.Revision) + 1,
            DeletedAt = deletedAt,
        };

    public static ShoppingItem WithConcurrentCreationMetadata(
        ShoppingItem chosen,
        ShoppingItem local,
        ShoppingItem remote,
        DateTimeOffset mergedAt) =>
        chosen with
        {
            CreatedAt = Earlier(local.CreatedAt, remote.CreatedAt),
            UpdatedAt = mergedAt,
            Revision = Math.Max(local.Revision, remote.Revision) + 1,
            DeletedAt = EarlierDeletedAt(local.DeletedAt, remote.DeletedAt),
        };

    private static DateTimeOffset? EarlierDeletedAt(DateTimeOffset? local, DateTimeOffset? remote)
    {
        if (local is not null && remote is not null)
        {
            return local <= remote ? local : remote;
        }

        return local ?? remote;
    }

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second, DateTimeOffset third)
    {
        var earliest = first <= second ? first : second;
        return earliest <= third ? earliest : third;
    }
}
