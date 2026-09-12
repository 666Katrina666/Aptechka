using Aptechka.Domain.Inventory;

namespace Aptechka.Infrastructure.Sync;

internal static class SnapshotMergeShopping
{
    public const string PathPrefix = "shopping/";

    public static bool UsersEqual(ShoppingItem left, ShoppingItem right) =>
        left.IsRequested == right.IsRequested &&
        left.Note == right.Note &&
        (left.DeletedAt is not null) == (right.DeletedAt is not null);

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

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second, DateTimeOffset third)
    {
        var earliest = first <= second ? first : second;
        return earliest <= third ? earliest : third;
    }
}
