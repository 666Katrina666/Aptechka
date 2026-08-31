namespace Aptechka.Application.Inventory;

public interface IIdGenerator
{
    string Create(DateTimeOffset timestamp);
}
