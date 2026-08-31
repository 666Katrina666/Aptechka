using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed record InventoryItemDraft(
    string Name,
    InventoryItemCategory Category,
    IReadOnlyList<string> ActiveIngredients,
    string? Form,
    string? Strength,
    string? Description,
    bool KeepInStock);
