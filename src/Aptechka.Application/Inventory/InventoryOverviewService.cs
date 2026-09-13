using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public enum ExpirationAttentionWindow
{
    Within7,
    Within30,
    Within90,
}

public sealed record AttentionEntry(
    string ItemId,
    string Name,
    string? Form,
    string? Strength,
    ItemAvailability Availability,
    int ExpiredPackageCount,
    DateOnly? NearestExpirationDate,
    ExpirationAttentionWindow? ExpirationWindow,
    bool HasKeepInStockMissing,
    bool HasLowStock);

public sealed record CatalogItemOverview(
    InventoryItem Item,
    ItemStockSummary Summary);

public sealed record InventoryOverview(
    IReadOnlyList<CatalogItemOverview> Catalog,
    IReadOnlyList<AttentionEntry> Attention);

public sealed class InventoryOverviewService(
    IInventoryRepository items,
    IPackageRepository packages,
    IClock clock)
{
    public const int Within7Days = 7;
    public const int Within30Days = 30;
    public const int Within90Days = 90;

    public async Task<InventoryOverview> GetAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var catalog = await items.GetItemsAsync(cancellationToken);
        var allPackages = await packages.GetPackagesAsync(cancellationToken);
        return Build(catalog, allPackages, query, clock.Today);
    }

    internal static InventoryOverview Build(
        IReadOnlyList<InventoryItem> catalog,
        IReadOnlyList<Package> allPackages,
        string? query,
        DateOnly today)
    {
        var packagesByItem = allPackages
            .GroupBy(static package => package.ItemId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        var active = new List<(InventoryItem Item, ItemStockSummary Summary)>();
        foreach (var item in catalog)
        {
            if (item.DeletedAt is not null)
            {
                continue;
            }

            packagesByItem.TryGetValue(item.Id, out var itemPackages);
            active.Add((item, ItemStock.Summarize(itemPackages ?? [], today)));
        }

        var filtered = active
            .Where(entry => InventorySearch.Matches(entry.Item, query))
            .OrderBy(static entry => entry.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => new CatalogItemOverview(entry.Item, entry.Summary))
            .ToArray();

        var attention = active
            .Select(entry => TryCreate(entry.Item, entry.Summary, today))
            .OfType<AttentionEntry>()
            .OrderBy(static entry => Rank(entry))
            .ThenBy(static entry => entry.NearestExpirationDate is null)
            .ThenBy(static entry => entry.NearestExpirationDate)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ItemId, StringComparer.Ordinal)
            .ToArray();

        return new InventoryOverview(filtered, attention);
    }

    private static AttentionEntry? TryCreate(
        InventoryItem item,
        ItemStockSummary summary,
        DateOnly today)
    {
        var window = ClassifyWindow(today, summary.NearestExpirationDate);
        var keepMissing = item.KeepInStock && summary.Availability == ItemAvailability.Missing;
        var low = summary.Availability == ItemAvailability.Low;
        if (summary.ExpiredPackageCount <= 0 && !keepMissing && window is null && !low)
        {
            return null;
        }

        return new AttentionEntry(
            item.Id,
            item.Name,
            item.Form,
            item.Strength,
            summary.Availability,
            summary.ExpiredPackageCount,
            summary.NearestExpirationDate,
            window,
            keepMissing,
            low);
    }

    private static ExpirationAttentionWindow? ClassifyWindow(DateOnly today, DateOnly? nearest)
    {
        if (nearest is not { } date || date < today)
        {
            return null;
        }

        var days = date.DayNumber - today.DayNumber;
        if (days <= Within7Days)
        {
            return ExpirationAttentionWindow.Within7;
        }

        if (days <= Within30Days)
        {
            return ExpirationAttentionWindow.Within30;
        }

        return days <= Within90Days ? ExpirationAttentionWindow.Within90 : null;
    }

    private static int Rank(AttentionEntry entry)
    {
        if (entry.ExpiredPackageCount > 0)
        {
            return 0;
        }

        if (entry.HasKeepInStockMissing)
        {
            return 1;
        }

        if (entry.ExpirationWindow == ExpirationAttentionWindow.Within7)
        {
            return 2;
        }

        if (entry.HasLowStock)
        {
            return 3;
        }

        return entry.ExpirationWindow == ExpirationAttentionWindow.Within30 ? 4 : 5;
    }
}
