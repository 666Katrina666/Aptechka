using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public interface IProblemRepository
{
    Task<IReadOnlyList<Problem>> GetProblemsAsync(CancellationToken cancellationToken = default);

    Task SaveProblemAsync(Problem problem, CancellationToken cancellationToken = default);
}
