using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Inventory;

public interface IPackageRepository
{
    Task<IReadOnlyList<Package>> GetPackagesAsync(CancellationToken cancellationToken = default);

    Task SavePackageAsync(Package package, CancellationToken cancellationToken = default);
}
