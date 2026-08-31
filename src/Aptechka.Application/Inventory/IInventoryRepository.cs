using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public interface IInventoryRepository
{
    Task<IReadOnlyList<InventoryItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    Task SaveItemAsync(InventoryItem item, CancellationToken cancellationToken = default);
}
