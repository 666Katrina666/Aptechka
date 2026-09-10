using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Domain.Identity;

namespace Aptechka.Infrastructure.Storage;

public sealed class FileInventoryRepository(
    string rootPath,
    IClock clock,
    IIdGenerator idGenerator) : IInventoryRepository, IPackageRepository, IDataSnapshotStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string manifestPath = Path.Combine(rootPath, "aptechka.json");
    private readonly string itemsPath = Path.Combine(rootPath, "items");
    private readonly string packagesPath = Path.Combine(rootPath, "packages");

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

    public async Task<IReadOnlyList<Package>> GetPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(packagesPath))
            {
                return [];
            }

            var packages = new List<Package>();
            foreach (var path in Directory.EnumerateFiles(packagesPath, "*.json"))
            {
                var package = await ReadJsonAsync<Package>(path, cancellationToken);
                package.EnsureValid();
                packages.Add(package);
            }

            return packages.OrderBy(static package => package.CreatedAt).ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SavePackageAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        package.EnsureValid();

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(packagesPath);
            await EnsureManifestAsync(cancellationToken);

            var packagePath = Path.Combine(packagesPath, $"{package.Id}.json");
            await WriteJsonAtomicallyAsync(packagePath, package, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(rootPath);
            await EnsureManifestAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DataSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnlockedAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReplaceAsync(
        DataSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await ReplaceUnlockedAsync(snapshot, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> TryReplaceAsync(
        DataSnapshot expected,
        DataSnapshot replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadUnlockedAsync(cancellationToken);
            if (!current.HasSameFiles(expected))
            {
                return false;
            }

            await ReplaceUnlockedAsync(replacement, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DataSnapshot> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            return DataSnapshot.Empty;
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
            if (!IsManagedPath(relativePath))
            {
                continue;
            }

            files.Add(relativePath, await File.ReadAllBytesAsync(path, cancellationToken));
        }

        return new DataSnapshot(files);
    }

    private async Task ReplaceUnlockedAsync(
        DataSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var parentPath = Path.GetDirectoryName(rootPath)
            ?? throw new InvalidOperationException("У каталога данных отсутствует родитель.");
        Directory.CreateDirectory(parentPath);

        var operationId = Guid.NewGuid().ToString("N");
        var stagingPath = $"{rootPath}.staging-{operationId}";
        var backupPath = $"{rootPath}.backup-{operationId}";

        try
        {
            Directory.CreateDirectory(stagingPath);
            foreach (var (relativePath, content) in snapshot.Files)
            {
                if (!IsManagedPath(relativePath))
                {
                    throw new InvalidDataException($"Недопустимый путь в снимке: {relativePath}");
                }

                var destinationPath = Path.Combine(
                    stagingPath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllBytesAsync(destinationPath, content, cancellationToken);
            }

            await ValidateSnapshotAsync(stagingPath, cancellationToken);

            if (Directory.Exists(rootPath))
            {
                Directory.Move(rootPath, backupPath);
            }

            try
            {
                Directory.Move(stagingPath, rootPath);
            }
            catch
            {
                if (Directory.Exists(backupPath) && !Directory.Exists(rootPath))
                {
                    Directory.Move(backupPath, rootPath);
                }

                throw;
            }

            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            if (Directory.Exists(backupPath) && Directory.Exists(rootPath))
            {
                Directory.Delete(backupPath, true);
            }
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

    private static async Task ValidateSnapshotAsync(
        string snapshotRoot,
        CancellationToken cancellationToken)
    {
        var snapshotManifestPath = Path.Combine(snapshotRoot, "aptechka.json");
        if (!File.Exists(snapshotManifestPath))
        {
            throw new InvalidDataException("В снимке отсутствует aptechka.json.");
        }

        var manifest = await ReadJsonAsync<DatasetManifest>(snapshotManifestPath, cancellationToken);
        manifest.EnsureCompatible();

        var items = new Dictionary<string, InventoryItem>(StringComparer.Ordinal);
        var snapshotItemsPath = Path.Combine(snapshotRoot, "items");
        if (Directory.Exists(snapshotItemsPath))
        {
            foreach (var itemPath in Directory.EnumerateFiles(snapshotItemsPath, "*.json"))
            {
                var item = await ReadJsonAsync<InventoryItem>(itemPath, cancellationToken);
                item.EnsureValid();
                EnsureFileNameMatchesId(itemPath, item.Id, "позиции");
                items[item.Id] = item;
            }
        }

        var snapshotPackagesPath = Path.Combine(snapshotRoot, "packages");
        if (!Directory.Exists(snapshotPackagesPath))
        {
            return;
        }

        foreach (var packagePath in Directory.EnumerateFiles(snapshotPackagesPath, "*.json"))
        {
            var package = await ReadJsonAsync<Package>(packagePath, cancellationToken);
            package.EnsureValid();
            EnsureFileNameMatchesId(packagePath, package.Id, "упаковки");
            if (!items.TryGetValue(package.ItemId, out var item))
            {
                throw new InvalidDataException("Упаковка ссылается на отсутствующую позицию.");
            }

            if (package.DeletedAt is null && item.DeletedAt is not null)
            {
                throw new InvalidDataException("Активная упаковка не может ссылаться на архивную позицию.");
            }
        }
    }

    private static void EnsureFileNameMatchesId(string path, string id, string entityName)
    {
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Имя файла {entityName} не совпадает с её ULID.");
        }
    }

    private static bool IsManagedPath(string relativePath)
    {
        if (string.Equals(relativePath, "aptechka.json", StringComparison.Ordinal))
        {
            return true;
        }

        return IsEntityJsonPath(relativePath, "items/") ||
               IsEntityJsonPath(relativePath, "packages/");
    }

    private static bool IsEntityJsonPath(string relativePath, string prefix)
    {
        if (!relativePath.StartsWith(prefix, StringComparison.Ordinal) ||
            !relativePath.EndsWith(".json", StringComparison.Ordinal))
        {
            return false;
        }

        var id = relativePath[prefix.Length..^".json".Length];
        return !id.Contains('/') && UlidGenerator.IsValid(id);
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
