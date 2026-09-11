using Aptechka.Domain.Inventory;

namespace Aptechka.App;

internal static class ProblemNameConflictChoice
{
    public const string CreateAnyway = "Всё равно создать";

    public static IReadOnlyList<string> OpenLabels(IReadOnlyList<Problem> conflicts)
    {
        var labels = conflicts.Select(OpenLabel).ToArray();
        if (labels.Distinct(StringComparer.Ordinal).Count() == labels.Length)
        {
            return labels;
        }

        return conflicts
            .Select((problem, index) => $"{OpenLabel(problem)} [{index + 1}]")
            .ToArray();
    }

    public static string OpenLabel(Problem problem)
    {
        var alias = problem.Aliases.FirstOrDefault();
        return string.IsNullOrEmpty(alias)
            ? $"Открыть «{problem.Name}»"
            : $"Открыть «{problem.Name}» ({alias})";
    }

    public static string? ResolveOpenedProblemId(
        IReadOnlyList<Problem> conflicts,
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
