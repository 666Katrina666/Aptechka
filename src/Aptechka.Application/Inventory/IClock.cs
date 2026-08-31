namespace Aptechka.Application.Inventory;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
