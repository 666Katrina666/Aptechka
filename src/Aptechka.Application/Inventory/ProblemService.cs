using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public sealed class ProblemService(
    IProblemRepository repository,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<IReadOnlyList<Problem>> GetCatalogAsync(
        CancellationToken cancellationToken = default) =>
        await SearchAsync(null, cancellationToken);

    public async Task<Problem?> GetProblemAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var problems = await repository.GetProblemsAsync(cancellationToken);
        return problems.FirstOrDefault(problem => problem.Id == id);
    }

    public async Task<IReadOnlyList<Problem>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var problems = await repository.GetProblemsAsync(cancellationToken);
        IEnumerable<Problem> matches = problems.Where(static problem => problem.DeletedAt is null);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            matches = matches.Where(problem => Matches(problem, term));
        }

        return matches
            .OrderBy(static problem => problem.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<Problem>> FindNameMatchesAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var term = name.Trim();
        var problems = await repository.GetProblemsAsync(cancellationToken);
        return problems
            .Where(problem => problem.DeletedAt is null && HasNameMatch(problem, term))
            .OrderBy(static problem => problem.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<Problem> CreateAsync(
        ProblemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var problem = Problem.Create(
            idGenerator.Create(now),
            now,
            draft.Name,
            draft.Aliases,
            draft.ItemIds,
            draft.Note);

        await repository.SaveProblemAsync(problem, cancellationToken);
        return problem;
    }

    public async Task<Problem> UpdateAsync(
        string id,
        ProblemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredActiveProblemAsync(id, cancellationToken);
        var problem = existing.Update(
            clock.UtcNow,
            draft.Name,
            draft.Aliases,
            draft.ItemIds,
            draft.Note);

        await repository.SaveProblemAsync(problem, cancellationToken);
        return problem;
    }

    public async Task<Problem> ArchiveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredProblemAsync(id, cancellationToken);
        var problem = existing.Delete(clock.UtcNow);
        await repository.SaveProblemAsync(problem, cancellationToken);
        return problem;
    }

    public async Task<Problem> LinkItemAsync(
        string id,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredActiveProblemAsync(id, cancellationToken);
        if (existing.ItemIds.Contains(itemId, StringComparer.Ordinal))
        {
            return existing;
        }

        var problem = existing.Update(
            clock.UtcNow,
            existing.Name,
            existing.Aliases,
            existing.ItemIds.Append(itemId).ToArray(),
            existing.Note);

        await repository.SaveProblemAsync(problem, cancellationToken);
        return problem;
    }

    public async Task<Problem> UnlinkItemAsync(
        string id,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredActiveProblemAsync(id, cancellationToken);
        if (!existing.ItemIds.Contains(itemId, StringComparer.Ordinal))
        {
            return existing;
        }

        var problem = existing.Update(
            clock.UtcNow,
            existing.Name,
            existing.Aliases,
            existing.ItemIds.Where(existingId => existingId != itemId).ToArray(),
            existing.Note);

        await repository.SaveProblemAsync(problem, cancellationToken);
        return problem;
    }

    private async Task<Problem> GetRequiredProblemAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var problem = await GetProblemAsync(id, cancellationToken);
        return problem ?? throw new InvalidOperationException("Проблема не найдена.");
    }

    private async Task<Problem> GetRequiredActiveProblemAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var existing = await GetRequiredProblemAsync(id, cancellationToken);
        if (existing.DeletedAt is not null)
        {
            throw new InvalidOperationException("Нельзя изменить архивную проблему.");
        }

        return existing;
    }

    private static bool Matches(Problem problem, string term) =>
        problem.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        problem.Aliases.Any(alias => alias.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool HasNameMatch(Problem problem, string term) =>
        string.Equals(problem.Name, term, StringComparison.OrdinalIgnoreCase) ||
        problem.Aliases.Any(alias => string.Equals(alias, term, StringComparison.OrdinalIgnoreCase));
}
