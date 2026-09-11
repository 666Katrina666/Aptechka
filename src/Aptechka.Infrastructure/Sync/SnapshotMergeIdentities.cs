using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Infrastructure.Sync;

internal static class SnapshotMergeIdentities
{
    public const string ManifestPath = "aptechka.json";

    public static DatasetManifest RequireManifest(DataSnapshot snapshot, string sideName)
    {
        if (!snapshot.Files.TryGetValue(ManifestPath, out var bytes))
        {
            throw new InvalidDataException($"В {sideName} снимке отсутствует {ManifestPath}.");
        }

        return ReadManifest(ManifestPath, bytes);
    }

    public static DatasetManifest ReadManifest(string path, byte[] bytes)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<DatasetManifest>(bytes, AptechkaJson.Options)
                ?? throw new InvalidDataException($"Файл данных пуст: {path}");
            manifest.EnsureCompatible();
            return manifest;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Некорректный JSON: {path}", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException($"Некорректный JSON: {path}", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Несовместимый манифест: {path}", exception);
        }
    }

    public static void EnsurePackageItemIdImmutable(
        string path,
        Package @base,
        Package? local,
        Package? remote)
    {
        if (local is not null &&
            !string.Equals(local.ItemId, @base.ItemId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Нельзя изменить itemId упаковки: {path}");
        }

        if (remote is not null &&
            !string.Equals(remote.ItemId, @base.ItemId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Нельзя изменить itemId упаковки: {path}");
        }
    }

    public static void EnsureProblemIdImmutable(
        string path,
        Problem @base,
        Problem? local,
        Problem? remote)
    {
        if (local is not null &&
            !string.Equals(local.Id, @base.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Нельзя изменить id проблемы: {path}");
        }

        if (remote is not null &&
            !string.Equals(remote.Id, @base.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Нельзя изменить id проблемы: {path}");
        }
    }
}
