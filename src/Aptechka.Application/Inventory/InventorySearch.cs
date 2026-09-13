using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public static class InventorySearch
{
    public static bool Matches(InventoryItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var term = query.Trim();
        return item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               item.Aliases.Any(alias => alias.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
               item.ActiveIngredients.Any(ingredient =>
                   ingredient.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
