namespace Aptechka.Domain.Inventory;

public enum PackageUsability
{
    Usable,
    Depleted,
    Expired,
}

public enum ItemAvailability
{
    Available,
    Low,
    Missing,
}

public sealed record ItemStockSummary(
    ItemAvailability Availability,
    int UsablePackageCount,
    DateOnly? NearestExpirationDate,
    int ExpiredPackageCount);

public static class ItemStock
{
    public static DateOnly? GetExpirationAfterOpening(Package package) =>
        package.OpenedDate is { } openedDate && package.ShelfLifeAfterOpeningDays is { } days
            ? openedDate.AddDays(days)
            : null;

    public static DateOnly? GetEffectiveExpirationDate(Package package)
    {
        var labeled = package.ExpirationDate;
        var afterOpening = GetExpirationAfterOpening(package);
        if (labeled is { } labeledDate && afterOpening is { } afterOpeningDate)
        {
            return labeledDate < afterOpeningDate ? labeledDate : afterOpeningDate;
        }

        return labeled ?? afterOpening;
    }

    public static bool IsExpired(Package package, DateOnly today) =>
        GetEffectiveExpirationDate(package) is { } expiration && today > expiration;

    public static bool IsUsable(Package package, DateOnly today) =>
        package.DeletedAt is null &&
        package.StockState != StockState.Depleted &&
        !IsExpired(package, today);

    public static PackageUsability GetUsability(Package package, DateOnly today)
    {
        if (package.StockState == StockState.Depleted)
        {
            return PackageUsability.Depleted;
        }

        return IsExpired(package, today) ? PackageUsability.Expired : PackageUsability.Usable;
    }

    public static ItemStockSummary Summarize(IEnumerable<Package> packages, DateOnly today)
    {
        var active = packages.Where(static package => package.DeletedAt is null).ToArray();
        var usable = active.Where(package => IsUsable(package, today)).ToArray();
        var availability = usable.Any(static package => package.StockState == StockState.Available)
            ? ItemAvailability.Available
            : usable.Any(static package => package.StockState == StockState.Low)
                ? ItemAvailability.Low
                : ItemAvailability.Missing;

        DateOnly? nearest = null;
        foreach (var package in usable)
        {
            if (GetEffectiveExpirationDate(package) is { } expiration &&
                (nearest is null || expiration < nearest))
            {
                nearest = expiration;
            }
        }

        return new ItemStockSummary(
            availability,
            usable.Length,
            nearest,
            active.Count(package => package.StockState != StockState.Depleted && IsExpired(package, today)));
    }
}
