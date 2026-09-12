using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public interface IShoppingItemRepository
{
    Task<IReadOnlyList<ShoppingItem>> GetShoppingItemsAsync(CancellationToken cancellationToken = default);

    Task SaveShoppingItemAsync(ShoppingItem shoppingItem, CancellationToken cancellationToken = default);
}
