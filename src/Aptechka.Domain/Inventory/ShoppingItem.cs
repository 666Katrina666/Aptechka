using Aptechka.Domain.Identity;

namespace Aptechka.Domain.Inventory;

public sealed record ShoppingItem(
    string Id,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string ItemId,
    bool IsRequested,
    string? Note)
{
    public static ShoppingItem Create(
        string itemId,
        DateTimeOffset now,
        bool isRequested,
        string? note)
    {
        var shopping = new ShoppingItem(
            itemId,
            1,
            now,
            now,
            null,
            itemId,
            isRequested,
            NormalizeOptional(note));

        shopping.EnsureValid();
        return shopping;
    }

    public ShoppingItem Update(DateTimeOffset now, bool isRequested, string? note)
    {
        var normalizedNote = NormalizeOptional(note);
        if (IsRequested == isRequested && Note == normalizedNote)
        {
            return this;
        }

        var shopping = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            IsRequested = isRequested,
            Note = normalizedNote,
        };

        shopping.EnsureValid();
        return shopping;
    }

    public ShoppingItem Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return this;
        }

        var shopping = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            DeletedAt = now,
        };

        shopping.EnsureValid();
        return shopping;
    }

    public void EnsureValid()
    {
        if (!UlidGenerator.IsValid(Id) || !UlidGenerator.IsValid(ItemId))
        {
            throw new InvalidOperationException("Идентификатор отметки покупки не является ULID.");
        }

        if (!string.Equals(Id, ItemId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("id отметки покупки должен совпадать с itemId.");
        }

        if (Revision < 1)
        {
            throw new InvalidOperationException("Ревизия отметки покупки должна быть положительной.");
        }

        if (UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата обновления не может быть раньше даты создания.");
        }

        if (DeletedAt is { } deletedAt && deletedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата удаления не может быть раньше даты создания.");
        }

        EnsureNoteValid();
    }

    private void EnsureNoteValid()
    {
        if (Note is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Note) || Note != Note.Trim())
        {
            throw new InvalidOperationException("Заметка покупки должна быть пустой или без внешних пробелов.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
