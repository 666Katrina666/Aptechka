using Aptechka.Domain.Identity;

namespace Aptechka.Domain.Inventory;

public enum StockState
{
    Available,
    Low,
    Depleted,
}

public enum ExpirationPrecision
{
    Day,
    Month,
}

public sealed record Package(
    string Id,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string ItemId,
    string? Label,
    DateOnly? ExpirationDate,
    ExpirationPrecision? ExpirationPrecision,
    DateOnly? OpenedDate,
    int? ShelfLifeAfterOpeningDays,
    StockState StockState,
    string? Note)
{
    public static Package Create(
        string id,
        DateTimeOffset now,
        string itemId,
        string? label,
        DateOnly? expirationDate,
        ExpirationPrecision? expirationPrecision,
        DateOnly? openedDate,
        int? shelfLifeAfterOpeningDays,
        StockState stockState,
        string? note)
    {
        var package = new Package(
            id,
            1,
            now,
            now,
            null,
            itemId,
            NormalizeOptional(label),
            expirationDate,
            expirationPrecision,
            openedDate,
            shelfLifeAfterOpeningDays,
            stockState,
            NormalizeOptional(note));

        package.EnsureValid();
        return package;
    }

    public Package Update(
        DateTimeOffset now,
        string? label,
        DateOnly? expirationDate,
        ExpirationPrecision? expirationPrecision,
        DateOnly? openedDate,
        int? shelfLifeAfterOpeningDays,
        StockState stockState,
        string? note)
    {
        var package = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            Label = NormalizeOptional(label),
            ExpirationDate = expirationDate,
            ExpirationPrecision = expirationPrecision,
            OpenedDate = openedDate,
            ShelfLifeAfterOpeningDays = shelfLifeAfterOpeningDays,
            StockState = stockState,
            Note = NormalizeOptional(note),
        };

        package.EnsureValid();
        return package;
    }

    public Package Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return this;
        }

        var package = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            DeletedAt = now,
        };

        package.EnsureValid();
        return package;
    }

    public void EnsureValid()
    {
        if (!UlidGenerator.IsValid(Id))
        {
            throw new InvalidOperationException("Идентификатор упаковки не является ULID.");
        }

        if (!UlidGenerator.IsValid(ItemId))
        {
            throw new InvalidOperationException("Идентификатор позиции упаковки не является ULID.");
        }

        if (Revision < 1)
        {
            throw new InvalidOperationException("Ревизия упаковки должна быть положительной.");
        }

        if (UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата обновления не может быть раньше даты создания.");
        }

        if (DeletedAt is { } deletedAt && deletedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата удаления не может быть раньше даты создания.");
        }

        if (ExpirationDate is null != ExpirationPrecision is null)
        {
            throw new InvalidOperationException("Срок годности и его точность задаются только вместе.");
        }

        if (ExpirationPrecision == global::Aptechka.Domain.Inventory.ExpirationPrecision.Month &&
            ExpirationDate is { } expiration &&
            expiration.Day != DateTime.DaysInMonth(expiration.Year, expiration.Month))
        {
            throw new InvalidOperationException("При точности до месяца срок должен быть последним днём месяца.");
        }

        if (ShelfLifeAfterOpeningDays is { } days && days < 1)
        {
            throw new InvalidOperationException("Срок после вскрытия должен быть строго положительным.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
