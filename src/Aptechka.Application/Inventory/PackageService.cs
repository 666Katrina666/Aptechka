using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed class PackageService(
    IPackageRepository packages,
    IInventoryRepository items,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<IReadOnlyList<Package>> GetPackagesForItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var all = await packages.GetPackagesAsync(cancellationToken);
        return all
            .Where(package => package.ItemId == itemId && package.DeletedAt is null)
            .OrderBy(static package => package.CreatedAt)
            .ToArray();
    }

    public async Task<Package?> GetPackageAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var all = await packages.GetPackagesAsync(cancellationToken);
        return all.FirstOrDefault(package => package.Id == id && package.DeletedAt is null);
    }

    public async Task<Package> CreateAsync(
        string itemId,
        PackageDraft draft,
        CancellationToken cancellationToken = default)
    {
        var item = await GetRequiredItemAsync(itemId, cancellationToken);
        if (item.DeletedAt is not null)
        {
            throw new InvalidOperationException("Нельзя создать упаковку для архивной позиции.");
        }

        var now = clock.UtcNow;
        var package = Package.Create(
            idGenerator.Create(now),
            now,
            item.Id,
            draft.Label,
            draft.ExpirationDate,
            draft.ExpirationPrecision,
            draft.OpenedDate,
            draft.ShelfLifeAfterOpeningDays,
            draft.StockState,
            draft.Note);

        await packages.SavePackageAsync(package, cancellationToken);
        return package;
    }

    public async Task<Package> UpdateAsync(
        string id,
        PackageDraft draft,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredActivePackageAsync(id, cancellationToken);
        var package = existing.Update(
            clock.UtcNow,
            draft.Label,
            draft.ExpirationDate,
            draft.ExpirationPrecision,
            draft.OpenedDate,
            draft.ShelfLifeAfterOpeningDays,
            draft.StockState,
            draft.Note);

        await packages.SavePackageAsync(package, cancellationToken);
        return package;
    }

    public async Task<Package> ArchiveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredPackageAsync(id, cancellationToken);
        var package = existing.Delete(clock.UtcNow);
        await packages.SavePackageAsync(package, cancellationToken);
        return package;
    }

    public Task<Package> MarkLowAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        UpdateStockStateAsync(id, StockState.Low, cancellationToken);

    public Task<Package> MarkDepletedAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        UpdateStockStateAsync(id, StockState.Depleted, cancellationToken);

    public async Task<ItemStockSummary> GetItemStockSummaryAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await GetRequiredItemAsync(itemId, cancellationToken);
        var itemPackages = await GetPackagesForItemAsync(itemId, cancellationToken);
        return ItemStock.Summarize(itemPackages, clock.Today);
    }

    private async Task<Package> UpdateStockStateAsync(
        string id,
        StockState stockState,
        CancellationToken cancellationToken)
    {
        var existing = await GetRequiredActivePackageAsync(id, cancellationToken);
        var package = existing.Update(
            clock.UtcNow,
            existing.Label,
            existing.ExpirationDate,
            existing.ExpirationPrecision,
            existing.OpenedDate,
            existing.ShelfLifeAfterOpeningDays,
            stockState,
            existing.Note);

        await packages.SavePackageAsync(package, cancellationToken);
        return package;
    }

    private async Task<InventoryItem> GetRequiredItemAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var all = await items.GetItemsAsync(cancellationToken);
        return all.FirstOrDefault(item => item.Id == itemId)
            ?? throw new InvalidOperationException("Позиция не найдена.");
    }

    private async Task<Package> GetRequiredPackageAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var all = await packages.GetPackagesAsync(cancellationToken);
        return all.FirstOrDefault(package => package.Id == id)
            ?? throw new InvalidOperationException("Упаковка не найдена.");
    }

    private async Task<Package> GetRequiredActivePackageAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var existing = await GetRequiredPackageAsync(id, cancellationToken);
        if (existing.DeletedAt is not null)
        {
            throw new InvalidOperationException("Нельзя изменить архивную упаковку.");
        }

        return existing;
    }
}
