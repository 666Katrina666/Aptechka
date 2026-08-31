using Aptechka.Domain.Identity;

namespace Aptechka.Domain.Inventory;

public sealed record DatasetManifest(
    int SchemaVersion,
    string DatasetId,
    DateTimeOffset CreatedAt)
{
    public const int CurrentSchemaVersion = 1;

    public void EnsureCompatible()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Версия данных {SchemaVersion} не поддерживается приложением.");
        }

        if (!UlidGenerator.IsValid(DatasetId))
        {
            throw new InvalidOperationException("datasetId не является ULID.");
        }
    }
}
