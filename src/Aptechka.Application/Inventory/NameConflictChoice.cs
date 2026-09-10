using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public static class NameConflictChoice
{
    public const string CreateAnyway = "Всё равно создать";

    public static IReadOnlyList<string> OpenLabels(IReadOnlyList<InventoryItem> conflicts)
    {
        var labels = conflicts.Select(OpenLabel).ToArray();
        if (labels.Distinct(StringComparer.Ordinal).Count() == labels.Length)
        {
            return labels;
        }

        return conflicts
            .Select((item, index) => $"{OpenLabel(item)} [{index + 1}]")
            .ToArray();
    }

    public static string OpenLabel(InventoryItem item)
    {
        var details = string.Join(
            ", ",
            new[]
            {
                item.Category == InventoryItemCategory.Medicine ? "лекарство" : "медицинский расходник",
                item.Form,
                item.Strength,
            }.Where(static value => !string.IsNullOrEmpty(value)));

        return string.IsNullOrEmpty(details)
            ? $"Открыть «{item.Name}»"
            : $"Открыть «{item.Name}» ({details})";
    }

    public static string? ResolveOpenedItemId(
        IReadOnlyList<InventoryItem> conflicts,
        string? action)
    {
        if (string.IsNullOrEmpty(action) ||
            string.Equals(action, CreateAnyway, StringComparison.Ordinal))
        {
            return null;
        }

        var labels = OpenLabels(conflicts);
        for (var index = 0; index < labels.Count; index++)
        {
            if (string.Equals(labels[index], action, StringComparison.Ordinal))
            {
                return conflicts[index].Id;
            }
        }

        return null;
    }
}
