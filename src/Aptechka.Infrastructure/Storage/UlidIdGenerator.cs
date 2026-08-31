using Aptechka.Application.Inventory;
using Aptechka.Domain.Identity;

namespace Aptechka.Infrastructure.Storage;

public sealed class UlidIdGenerator : IIdGenerator
{
    public string Create(DateTimeOffset timestamp) => UlidGenerator.Create(timestamp);
}
