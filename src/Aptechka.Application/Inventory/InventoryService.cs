using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed class InventoryService(
    IInventoryRepository repository,
    IPackageRepository packages,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<IReadOnlyList<InventoryItem>> GetCatalogAsync(
        CancellationToken cancellationToken = default) =>
        await SearchAsync(null, cancellationToken);

    public async Task<InventoryItem?> GetItemAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var items = await repository.GetItemsAsync(cancellationToken);
        return items.FirstOrDefault(item => item.Id == id);
    }

    public async Task<IReadOnlyList<InventoryItem>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var items = await repository.GetItemsAsync(cancellationToken);
        IEnumerable<InventoryItem> matches = items.Where(static item => item.DeletedAt is null);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            matches = matches.Where(item => Matches(item, term));
        }

        return matches
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<InventoryItem>> FindNameConflictsAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var term = name.Trim();
        var items = await repository.GetItemsAsync(cancellationToken);
        return items
            .Where(item => item.DeletedAt is null && HasNameConflict(item, term))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<InventoryItem> CreateAsync(
        InventoryItemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var item = InventoryItem.Create(
            idGenerator.Create(now),
            now,
            draft.Name,
            draft.Aliases,
            draft.Category,
            draft.ActiveIngredients,
            draft.Form,
            draft.Strength,
            draft.Description,
            draft.KeepInStock);

        await repository.SaveItemAsync(item, cancellationToken);
        return item;
    }

    public async Task<InventoryItem> UpdateAsync(
        string id,
        InventoryItemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredItemAsync(id, cancellationToken);
        if (existing.DeletedAt is not null)
        {
            throw new InvalidOperationException("Нельзя изменить архивную позицию.");
        }

        var item = existing.Update(
            clock.UtcNow,
            draft.Name,
            draft.Aliases,
            draft.Category,
            draft.ActiveIngredients,
            draft.Form,
            draft.Strength,
            draft.Description,
            draft.KeepInStock);

        await repository.SaveItemAsync(item, cancellationToken);
        return item;
    }

    public async Task<InventoryItem> ArchiveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredItemAsync(id, cancellationToken);
        var now = clock.UtcNow;
        var itemPackages = await packages.GetPackagesAsync(cancellationToken);
        foreach (var package in itemPackages.Where(package =>
                     package.ItemId == id && package.DeletedAt is null))
        {
            await packages.SavePackageAsync(package.Delete(now), cancellationToken);
        }

        var item = existing.Delete(now);
        await repository.SaveItemAsync(item, cancellationToken);
        return item;
    }

    private async Task<InventoryItem> GetRequiredItemAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(id, cancellationToken);
        return item ?? throw new InvalidOperationException("Позиция не найдена.");
    }

    private static bool Matches(InventoryItem item, string term) =>
        item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        item.Aliases.Any(alias => alias.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
        item.ActiveIngredients.Any(ingredient =>
            ingredient.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool HasNameConflict(InventoryItem item, string term) =>
        string.Equals(item.Name, term, StringComparison.OrdinalIgnoreCase) ||
        item.Aliases.Any(alias => string.Equals(alias, term, StringComparison.OrdinalIgnoreCase));
}
