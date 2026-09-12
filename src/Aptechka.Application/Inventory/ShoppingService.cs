using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed class ShoppingService(
    IShoppingItemRepository shoppingItems,
    IInventoryRepository items,
    IClock clock)
{
    public async Task<ShoppingItem?> GetByItemIdAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var records = await shoppingItems.GetShoppingItemsAsync(cancellationToken);
        return records.FirstOrDefault(record => record.ItemId == itemId);
    }

    public async Task<IReadOnlyList<ShoppingItem>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await shoppingItems.GetShoppingItemsAsync(cancellationToken);
        return records
            .Where(static record => record.DeletedAt is null)
            .OrderBy(static record => record.CreatedAt)
            .ToArray();
    }

    public async Task<ShoppingItem> RequestPurchaseAsync(
        string itemId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveInventoryItemAsync(itemId, cancellationToken);
        var existing = await TryGetMutableRecordAsync(itemId, cancellationToken);
        if (existing is null)
        {
            var created = ShoppingItem.Create(itemId, clock.UtcNow, true, note);
            await shoppingItems.SaveShoppingItemAsync(created, cancellationToken);
            return created;
        }

        var updated = existing.Update(clock.UtcNow, true, note);
        if (!ReferenceEquals(updated, existing))
        {
            await shoppingItems.SaveShoppingItemAsync(updated, cancellationToken);
        }

        return updated;
    }

    public async Task<ShoppingItem> ClearRequestAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveInventoryItemAsync(itemId, cancellationToken);
        var existing = await GetMutableRecordAsync(itemId, cancellationToken);
        var updated = existing.Update(clock.UtcNow, false, existing.Note);
        if (!ReferenceEquals(updated, existing))
        {
            await shoppingItems.SaveShoppingItemAsync(updated, cancellationToken);
        }

        return updated;
    }

    public async Task<ShoppingItem> UpdateNoteAsync(
        string itemId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveInventoryItemAsync(itemId, cancellationToken);
        var existing = await GetMutableRecordAsync(itemId, cancellationToken);
        var updated = existing.Update(clock.UtcNow, existing.IsRequested, note);
        if (!ReferenceEquals(updated, existing))
        {
            await shoppingItems.SaveShoppingItemAsync(updated, cancellationToken);
        }

        return updated;
    }

    private async Task EnsureActiveInventoryItemAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var catalog = await items.GetItemsAsync(cancellationToken);
        var item = catalog.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            throw new InvalidOperationException("Позиция не найдена.");
        }

        if (item.DeletedAt is not null)
        {
            throw new InvalidOperationException("Нельзя изменить отметку покупки для архивной позиции.");
        }
    }

    private async Task<ShoppingItem> GetMutableRecordAsync(
        string itemId,
        CancellationToken cancellationToken) =>
        await TryGetMutableRecordAsync(itemId, cancellationToken)
        ?? throw new InvalidOperationException("Отметка покупки не найдена.");

    private async Task<ShoppingItem?> TryGetMutableRecordAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var existing = await GetByItemIdAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.DeletedAt is not null)
        {
            throw new InvalidOperationException("Нельзя изменить архивную отметку покупки.");
        }

        return existing;
    }
}
