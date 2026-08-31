using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Infrastructure.Storage;

public sealed class FileInventoryRepository(
    string rootPath,
    IClock clock,
    IIdGenerator idGenerator) : IInventoryRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string manifestPath = Path.Combine(rootPath, "aptechka.json");
    private readonly string itemsPath = Path.Combine(rootPath, "items");

    public async Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(itemsPath))
            {
                return [];
            }

            var items = new List<InventoryItem>();
            foreach (var path in Directory.EnumerateFiles(itemsPath, "*.json"))
            {
                var item = await ReadJsonAsync<InventoryItem>(path, cancellationToken);
                item.EnsureValid();
                items.Add(item);
            }

            return items.OrderBy(static item => item.CreatedAt).ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveItemAsync(
        InventoryItem item,
        CancellationToken cancellationToken = default)
    {
        item.EnsureValid();

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(itemsPath);
            await EnsureManifestAsync(cancellationToken);

            var itemPath = Path.Combine(itemsPath, $"{item.Id}.json");
            await WriteJsonAtomicallyAsync(itemPath, item, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureManifestAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(manifestPath))
        {
            var existing = await ReadJsonAsync<DatasetManifest>(manifestPath, cancellationToken);
            existing.EnsureCompatible();
            return;
        }

        var now = clock.UtcNow;
        var manifest = new DatasetManifest(
            DatasetManifest.CurrentSchemaVersion,
            idGenerator.Create(now),
            now);

        await WriteJsonAtomicallyAsync(manifestPath, manifest, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, AptechkaJson.Options, cancellationToken)
            ?? throw new InvalidDataException($"Файл данных пуст: {Path.GetFileName(path)}");
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    AptechkaJson.Options,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
