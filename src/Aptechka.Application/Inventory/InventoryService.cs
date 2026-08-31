using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed class InventoryService(
    IInventoryRepository repository,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<InventoryItem?> GetPrototypeItemAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await repository.GetItemsAsync(cancellationToken);
        return items
            .Where(static item => item.DeletedAt is null)
            .OrderBy(static item => item.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<InventoryItem> SavePrototypeItemAsync(
        InventoryItemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var existing = await GetPrototypeItemAsync(cancellationToken);

        var item = existing is null
            ? InventoryItem.Create(
                idGenerator.Create(now),
                now,
                draft.Name,
                draft.Category,
                draft.ActiveIngredients,
                draft.Form,
                draft.Strength,
                draft.Description,
                draft.KeepInStock)
            : existing.Update(
                now,
                draft.Name,
                draft.Category,
                draft.ActiveIngredients,
                draft.Form,
                draft.Strength,
                draft.Description,
                draft.KeepInStock);

        await repository.SaveItemAsync(item, cancellationToken);
        return item;
    }
}
