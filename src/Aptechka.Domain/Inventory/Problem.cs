using Aptechka.Domain.Identity;

namespace Aptechka.Domain.Inventory;

public sealed record Problem(
    string Id,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> ItemIds,
    string? Note)
{
    public static Problem Create(
        string id,
        DateTimeOffset now,
        string name,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> itemIds,
        string? note)
    {
        var problem = new Problem(
            id,
            1,
            now,
            now,
            null,
            NormalizeRequired(name),
            NormalizeAliases(aliases),
            NormalizeItemIds(itemIds),
            NormalizeOptional(note));

        problem.EnsureValid();
        return problem;
    }

    public Problem Update(
        DateTimeOffset now,
        string name,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> itemIds,
        string? note)
    {
        var problem = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            Name = NormalizeRequired(name),
            Aliases = NormalizeAliases(aliases),
            ItemIds = NormalizeItemIds(itemIds),
            Note = NormalizeOptional(note),
        };

        problem.EnsureValid();
        return problem;
    }

    public Problem Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return this;
        }

        var problem = this with
        {
            Revision = Revision + 1,
            UpdatedAt = now,
            DeletedAt = now,
        };

        problem.EnsureValid();
        return problem;
    }

    public void EnsureValid()
    {
        if (!UlidGenerator.IsValid(Id))
        {
            throw new InvalidOperationException("Идентификатор проблемы не является ULID.");
        }

        if (Revision < 1)
        {
            throw new InvalidOperationException("Ревизия проблемы должна быть положительной.");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name != Name.Trim())
        {
            throw new InvalidOperationException("Название проблемы не может быть пустым.");
        }

        if (UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата обновления не может быть раньше даты создания.");
        }

        if (DeletedAt is { } deletedAt && deletedAt < CreatedAt)
        {
            throw new InvalidOperationException("Дата удаления не может быть раньше даты создания.");
        }

        EnsureAliasesValid();
        EnsureItemIdsValid();
        EnsureNoteValid();
    }

    private void EnsureAliasesValid()
    {
        if (Aliases is null)
        {
            throw new InvalidOperationException("Список алиасов проблемы отсутствует.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in Aliases)
        {
            if (alias is null || string.IsNullOrWhiteSpace(alias) || alias != alias.Trim())
            {
                throw new InvalidOperationException("Алиасы проблемы должны быть непустыми и без внешних пробелов.");
            }

            if (!seen.Add(alias))
            {
                throw new InvalidOperationException("Алиасы проблемы не должны повторяться.");
            }
        }
    }

    private void EnsureItemIdsValid()
    {
        if (ItemIds is null)
        {
            throw new InvalidOperationException("Список связей проблемы отсутствует.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemId in ItemIds)
        {
            if (itemId is null ||
                itemId.Length == 0 ||
                itemId != itemId.Trim() ||
                !UlidGenerator.IsValid(itemId))
            {
                throw new InvalidOperationException("Связь проблемы должна ссылаться на ULID позиции.");
            }

            if (!seen.Add(itemId))
            {
                throw new InvalidOperationException("Связи проблемы не должны повторяться.");
            }
        }
    }

    private void EnsureNoteValid()
    {
        if (Note is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Note) || Note != Note.Trim())
        {
            throw new InvalidOperationException("Заметка проблемы должна быть пустой или без внешних пробелов.");
        }
    }

    private static string NormalizeRequired(string value) =>
        value.Trim() is { Length: > 0 } normalized
            ? normalized
            : throw new ArgumentException("Название проблемы не может быть пустым.", nameof(value));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeAliases(IEnumerable<string> values) =>
        values
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> NormalizeItemIds(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var itemId = value.Trim();
            if (itemId.Length == 0)
            {
                continue;
            }

            if (!UlidGenerator.IsValid(itemId))
            {
                throw new ArgumentException("Связь проблемы должна ссылаться на ULID позиции.", nameof(values));
            }

            if (seen.Add(itemId))
            {
                result.Add(itemId);
            }
        }

        return result;
    }
}
