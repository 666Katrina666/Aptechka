using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed record ShoppingEntry(
    string ItemId,
    string Name,
    string? Form,
    string? Strength,
    ItemAvailability Availability,
    bool HasManualReason,
    bool HasKeepInStockMissingReason,
    bool HasShoppingRecord,
    string? Note,
    DateTimeOffset? ManualUpdatedAt);

public sealed record ShoppingCandidate(
    string ItemId,
    string Name,
    string? Form,
    string? Strength,
    ItemAvailability Availability,
    bool HasKeepInStockMissingReason,
    bool IsRecentlyClosed);

public sealed record ShoppingListSnapshot(
    IReadOnlyList<ShoppingEntry> Active,
    IReadOnlyList<ShoppingEntry> RecentlyClosed,
    IReadOnlyList<ShoppingCandidate> Addable);
