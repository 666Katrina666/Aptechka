namespace Aptechka.Application.Inventory;

public sealed record ProblemDraft(
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> ItemIds,
    string? Note);
