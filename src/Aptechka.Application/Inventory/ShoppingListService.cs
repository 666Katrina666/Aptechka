using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed class ShoppingListService(
    IInventoryRepository items,
    IPackageRepository packages,
    IShoppingItemRepository shoppingItems,
    IClock clock)
{
    public async Task<ShoppingListSnapshot> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = await items.GetItemsAsync(cancellationToken);
        var allPackages = await packages.GetPackagesAsync(cancellationToken);
        var records = await shoppingItems.GetShoppingItemsAsync(cancellationToken);
        return Build(catalog, allPackages, records, clock.Today);
    }

    internal static ShoppingListSnapshot Build(
        IReadOnlyList<InventoryItem> catalog,
        IReadOnlyList<Package> allPackages,
        IReadOnlyList<ShoppingItem> records,
        DateOnly today)
    {
        var packagesByItem = allPackages
            .GroupBy(static package => package.ItemId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var shoppingByItem = records
            .GroupBy(static record => record.ItemId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        var active = new List<ShoppingEntry>();
        var recent = new List<ShoppingEntry>();
        var addable = new List<ShoppingCandidate>();

        foreach (var item in catalog)
        {
            if (item.DeletedAt is not null)
            {
                continue;
            }

            packagesByItem.TryGetValue(item.Id, out var itemPackages);
            var availability = ItemStock.Summarize(itemPackages ?? [], today).Availability;
            shoppingByItem.TryGetValue(item.Id, out var shopping);
            var liveShopping = shopping is { DeletedAt: null } ? shopping : null;
            var manual = liveShopping is { IsRequested: true };
            var keepInStockMissing = item.KeepInStock && availability == ItemAvailability.Missing;
            var entry = new ShoppingEntry(
                item.Id,
                item.Name,
                item.Form,
                item.Strength,
                availability,
                manual,
                keepInStockMissing,
                liveShopping is not null,
                liveShopping?.Note,
                liveShopping?.UpdatedAt);

            if (manual || keepInStockMissing)
            {
                active.Add(entry);
            }
            else if (liveShopping is { IsRequested: false })
            {
                recent.Add(entry);
            }

            if (!manual)
            {
                addable.Add(new ShoppingCandidate(
                    item.Id,
                    item.Name,
                    item.Form,
                    item.Strength,
                    availability,
                    keepInStockMissing,
                    liveShopping is { IsRequested: false }));
            }
        }

        return new ShoppingListSnapshot(
            SortActive(active),
            SortRecent(recent),
            addable
                .OrderBy(static candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static candidate => candidate.ItemId, StringComparer.Ordinal)
                .ToArray());
    }

    private static IReadOnlyList<ShoppingEntry> SortActive(List<ShoppingEntry> entries) =>
        entries
            .OrderBy(static entry => ActiveRank(entry))
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ItemId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ShoppingEntry> SortRecent(List<ShoppingEntry> entries) =>
        entries
            .OrderByDescending(static entry => entry.ManualUpdatedAt)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ItemId, StringComparer.Ordinal)
            .ToArray();

    private static int ActiveRank(ShoppingEntry entry)
    {
        if (entry.HasManualReason && entry.HasKeepInStockMissingReason)
        {
            return 0;
        }

        return entry.HasKeepInStockMissingReason ? 1 : 2;
    }
}
