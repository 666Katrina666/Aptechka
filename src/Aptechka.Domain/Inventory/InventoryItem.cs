using Aptechka.Domain.Identity;

namespace Aptechka.Domain.Inventory;

public sealed record InventoryItem(
    string Id,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string Name,
    IReadOnlyList<string> Aliases,
    InventoryItemCategory Category,
    IReadOnlyList<string> ActiveIngredients,
    string? Form,
    string? Strength,
    string? Description,
    bool KeepInStock,
    string? CoverPhotoId)
{
    public static InventoryItem Create(
        string id,
        DateTimeOffset now,
        string name,
        IReadOnlyList<string> aliases,
        InventoryItemCategory category,
        IReadOnlyList<string> activeIngredients,
        string? form,
        string? strength,
        string? description,
        bool keepInStock)
    {
        var item = new InventoryItem(
            id,
            1,
            now,
            now,
            null,
            NormalizeRequired(name),
            NormalizeList(aliases),
            category,
            NormalizeList(activeIngredients),
            NormalizeOptional(form),
            NormalizeOptional(strength),
            NormalizeOptional(description),
            keepInStock,
            null);

        item.EnsureValid();
        return item;
    }

    public InventoryItem Update(
        DateTimeOffset now,
        string name,
        IReadOnlyList<string> aliases,
        InventoryItemCategory category,
        IReadOnlyList<string> activeIngredients,
        string? form,
        string? strength,
        string? description,
        bool keepInStock)
    {
        var item = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            Name = NormalizeRequired(name),
            Aliases = NormalizeList(aliases),
            Category = category,
            ActiveIngredients = NormalizeList(activeIngredients),
            Form = NormalizeOptional(form),
            Strength = NormalizeOptional(strength),
            Description = NormalizeOptional(description),
            KeepInStock = keepInStock,
        };

        item.EnsureValid();
        return item;
    }

    public InventoryItem Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return this;
        }

        var item = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            DeletedAt = now,
        };

        item.EnsureValid();
        return item;
    }

    public void EnsureValid()
    {
        if (!UlidGenerator.IsValid(Id))
        {
            throw new InvalidOperationException("Идентификатор позиции не является ULID.");
        }

        if (Revision < 1)
        {
            throw new InvalidOperationException("Ревизия позиции должна быть положительной.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Название позиции не может быть пустым.");
        }

        if (UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата обновления не может быть раньше даты создания.");
        }

        if (DeletedAt is { } deletedAt && deletedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата удаления не может быть раньше даты создания.");
        }
    }

    private static string NormalizeRequired(string value) =>
        value.Trim() is { Length: > 0 } normalized
            ? normalized
            : throw new ArgumentException("Название позиции не может быть пустым.", nameof(value));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string> values) =>
        values
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
