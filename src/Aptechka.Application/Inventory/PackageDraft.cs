using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed record PackageDraft(
    string? Label,
    DateOnly? ExpirationDate,
    ExpirationPrecision? ExpirationPrecision,
    DateOnly? OpenedDate,
    int? ShelfLifeAfterOpeningDays,
    StockState StockState,
    string? Note);
