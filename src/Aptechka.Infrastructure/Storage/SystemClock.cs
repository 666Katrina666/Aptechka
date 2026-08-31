using Aptechka.Application.Inventory;

namespace Aptechka.Infrastructure.Storage;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
