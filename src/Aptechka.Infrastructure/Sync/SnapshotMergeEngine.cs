using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aptechka.Application.Sync;
using Aptechka.Domain.Identity;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Infrastructure.Sync;

public sealed class SnapshotMergeEngine
{
    private static readonly JsonSerializerOptions ConflictValueOptions = CreateConflictValueOptions();

    public SnapshotMergeResult Merge(
        DataSnapshot @base,
        DataSnapshot local,
        DataSnapshot remote,
        DateTimeOffset mergedAt)
    {
        ArgumentNullException.ThrowIfNull(@base);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var paths = @base.Files.Keys
            .Concat(local.Files.Keys)
            .Concat(remote.Files.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var conflicts = new List<SyncConflict>();
        var merged = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            @base.Files.TryGetValue(path, out var baseBytes);
            local.Files.TryGetValue(path, out var localBytes);
            remote.Files.TryGetValue(path, out var remoteBytes);
            MergePath(path, baseBytes, localBytes, remoteBytes, mergedAt, merged, conflicts);
        }

        if (conflicts.Count > 0)
        {
            conflicts.Sort(CompareConflicts);
            return new SnapshotMergeResult(null, conflicts);
        }

        return new SnapshotMergeResult(new DataSnapshot(merged), []);
    }

    private static void MergePath(
        string path,
        byte[]? baseBytes,
        byte[]? localBytes,
        byte[]? remoteBytes,
        DateTimeOffset mergedAt,
        Dictionary<string, byte[]> merged,
        List<SyncConflict> conflicts)
    {
        if (localBytes is null && remoteBytes is null)
        {
            return;
        }

        if (JsonEquals(path, localBytes, remoteBytes))
        {
            ValidatePresent(path, localBytes ?? remoteBytes);
            Accept(merged, path, localBytes ?? remoteBytes);
            return;
        }

        if (path.Equals("aptechka.json", StringComparison.Ordinal))
        {
            MergeManifest(path, baseBytes, localBytes, remoteBytes, merged, conflicts);
            return;
        }

        if (IsEntityPath(path, "items/", out _))
        {
            MergeItem(path, baseBytes, localBytes, remoteBytes, mergedAt, merged, conflicts);
            return;
        }

        if (IsEntityPath(path, "packages/", out _))
        {
            MergePackage(path, baseBytes, localBytes, remoteBytes, mergedAt, merged, conflicts);
            return;
        }

        if (path.StartsWith("items/", StringComparison.Ordinal) ||
            path.StartsWith("packages/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Путь не соответствует сущности: {path}");
        }

        MergeOpaqueFile(path, baseBytes, localBytes, remoteBytes, merged, conflicts);
    }

    private static void MergeManifest(
        string path,
        byte[]? baseBytes,
        byte[]? localBytes,
        byte[]? remoteBytes,
        Dictionary<string, byte[]> merged,
        List<SyncConflict> conflicts)
    {
        var baseManifest = ReadManifest(path, baseBytes);
        var localManifest = ReadManifest(path, localBytes);
        var remoteManifest = ReadManifest(path, remoteBytes);

        if (localManifest is not null &&
            remoteManifest is not null &&
            !string.Equals(localManifest.DatasetId, remoteManifest.DatasetId, StringComparison.Ordinal))
        {
            conflicts.Add(FileConflict(path, SyncConflictKind.FileChangedBoth, baseBytes, localBytes, remoteBytes));
            return;
        }

        MergeOpaqueFile(path, baseBytes, localBytes, remoteBytes, merged, conflicts);
        _ = baseManifest;
    }

    private static void MergeItem(
        string path,
        byte[]? baseBytes,
        byte[]? localBytes,
        byte[]? remoteBytes,
        DateTimeOffset mergedAt,
        Dictionary<string, byte[]> merged,
        List<SyncConflict> conflicts)
    {
        var @base = ReadItem(path, baseBytes);
        var local = ReadItem(path, localBytes);
        var remote = ReadItem(path, remoteBytes);
        if (TryMergeFilePresence(path, baseBytes, localBytes, remoteBytes, merged, conflicts))
        {
            return;
        }

        if (@base is null)
        {
            conflicts.Add(FileConflict(path, SyncConflictKind.FileChangedBoth, null, localBytes, remoteBytes));
            return;
        }

        if (ItemUsersEqual(local!, remote!))
        {
            AcceptItem(merged, path, @base, local!, remote!, localBytes!, mergedAt);
            return;
        }

        if (ItemUsersEqual(local!, @base))
        {
            Accept(merged, path, remoteBytes);
            return;
        }

        if (ItemUsersEqual(remote!, @base))
        {
            Accept(merged, path, localBytes);
            return;
        }

        if (IsDeleted(local!) != IsDeleted(remote!))
        {
            conflicts.Add(FieldConflict(
                path,
                "deletedAt",
                SyncConflictKind.DeleteVsModify,
                @base.DeletedAt,
                local!.DeletedAt,
                remote!.DeletedAt));
            return;
        }

        var fieldConflicts = new List<SyncConflict>();
        var name = MergeScalar(path, "name", @base.Name, local!.Name, remote!.Name, fieldConflicts);
        var category = MergeScalar(path, "category", @base.Category, local.Category, remote.Category, fieldConflicts);
        var form = MergeScalar(path, "form", @base.Form, local.Form, remote.Form, fieldConflicts);
        var strength = MergeScalar(path, "strength", @base.Strength, local.Strength, remote.Strength, fieldConflicts);
        var description = MergeScalar(
            path,
            "description",
            @base.Description,
            local.Description,
            remote.Description,
            fieldConflicts);
        var keepInStock = MergeScalar(
            path,
            "keepInStock",
            @base.KeepInStock,
            local.KeepInStock,
            remote.KeepInStock,
            fieldConflicts);
        var coverPhotoId = MergeScalar(
            path,
            "coverPhotoId",
            @base.CoverPhotoId,
            local.CoverPhotoId,
            remote.CoverPhotoId,
            fieldConflicts);
        var aliases = MergeStringSet(@base.Aliases, local.Aliases, remote.Aliases);
        var activeIngredients = MergeStringSet(
            @base.ActiveIngredients,
            local.ActiveIngredients,
            remote.ActiveIngredients);
        var deletedAt = MergeDeletedAt(@base, local, remote);

        if (fieldConflicts.Count > 0)
        {
            conflicts.AddRange(fieldConflicts);
            return;
        }

        var item = new InventoryItem(
            local.Id,
            Math.Max(local.Revision, remote.Revision) + 1,
            Earlier(@base.CreatedAt, local.CreatedAt, remote.CreatedAt),
            mergedAt,
            deletedAt,
            name!,
            aliases,
            category,
            activeIngredients,
            form,
            strength,
            description,
            keepInStock,
            coverPhotoId);
        ValidateEntity(path, item);
        Accept(merged, path, Serialize(item));
    }

    private static void MergePackage(
        string path,
        byte[]? baseBytes,
        byte[]? localBytes,
        byte[]? remoteBytes,
        DateTimeOffset mergedAt,
        Dictionary<string, byte[]> merged,
        List<SyncConflict> conflicts)
    {
        var @base = ReadPackage(path, baseBytes);
        var local = ReadPackage(path, localBytes);
        var remote = ReadPackage(path, remoteBytes);
        if (TryMergeFilePresence(path, baseBytes, localBytes, remoteBytes, merged, conflicts))
        {
            return;
        }

        if (@base is null)
        {
            conflicts.Add(FileConflict(path, SyncConflictKind.FileChangedBoth, null, localBytes, remoteBytes));
            return;
        }

        if (PackageUsersEqual(local!, remote!))
        {
            AcceptPackage(merged, path, @base, local!, remote!, localBytes!, mergedAt);
            return;
        }

        if (PackageUsersEqual(local!, @base))
        {
            Accept(merged, path, remoteBytes);
            return;
        }

        if (PackageUsersEqual(remote!, @base))
        {
            Accept(merged, path, localBytes);
            return;
        }

        if (!string.Equals(local!.ItemId, remote!.ItemId, StringComparison.Ordinal))
        {
            conflicts.Add(FieldConflict(
                path,
                "itemId",
                SyncConflictKind.FieldChangedBoth,
                @base.ItemId,
                local.ItemId,
                remote.ItemId));
            return;
        }

        if (IsDeleted(local) != IsDeleted(remote))
        {
            conflicts.Add(FieldConflict(
                path,
                "deletedAt",
                SyncConflictKind.DeleteVsModify,
                @base.DeletedAt,
                local.DeletedAt,
                remote.DeletedAt));
            return;
        }

        var fieldConflicts = new List<SyncConflict>();
        var label = MergeScalar(path, "label", @base.Label, local.Label, remote.Label, fieldConflicts);
        var expiration = MergeScalar(
            path,
            "expiration",
            ExpirationOf(@base),
            ExpirationOf(local),
            ExpirationOf(remote),
            fieldConflicts);
        var openedDate = MergeScalar(
            path,
            "openedDate",
            @base.OpenedDate,
            local.OpenedDate,
            remote.OpenedDate,
            fieldConflicts);
        var shelfLife = MergeScalar(
            path,
            "shelfLifeAfterOpeningDays",
            @base.ShelfLifeAfterOpeningDays,
            local.ShelfLifeAfterOpeningDays,
            remote.ShelfLifeAfterOpeningDays,
            fieldConflicts);
        var stockState = MergeScalar(
            path,
            "stockState",
            @base.StockState,
            local.StockState,
            remote.StockState,
            fieldConflicts);
        var note = MergeScalar(path, "note", @base.Note, local.Note, remote.Note, fieldConflicts);
        var deletedAt = MergeDeletedAt(@base, local, remote);

        if (fieldConflicts.Count > 0)
        {
            conflicts.AddRange(fieldConflicts);
            return;
        }

        var package = new Package(
            local.Id,
            Math.Max(local.Revision, remote.Revision) + 1,
            Earlier(@base.CreatedAt, local.CreatedAt, remote.CreatedAt),
            mergedAt,
            deletedAt,
            local.ItemId,
            label,
            expiration.Date,
            expiration.Precision,
            openedDate,
            shelfLife,
            stockState,
            note);
        ValidateEntity(path, package);
        Accept(merged, path, Serialize(package));
    }

    private static bool TryMergeFilePresence(
        string path,
        byte[]? baseBytes,
        byte[]? localBytes,
        byte[]? remoteBytes,
        Dictionary<string, byte[]> merged,
        List<SyncConflict> conflicts)
    {
        if (localBytes is null)
        {
            if (remoteBytes is null)
            {
                return true;
            }

            if (baseBytes is null)
            {
                Accept(merged, path, remoteBytes);
                return true;
            }

            if (JsonEquals(path, remoteBytes, baseBytes))
            {
                return true;
            }

            conflicts.Add(FileConflict(
                path,
                SyncConflictKind.FileDeleteVsModify,
                baseBytes,
                null,
                remoteBytes));
            return true;
        }

        if (remoteBytes is null)
        {
            if (baseBytes is null)
            {
                Accept(merged, path, localBytes);
                return true;
            }

            if (JsonEquals(path, localBytes, baseBytes))
            {
                return true;
            }

            conflicts.Add(FileConflict(
                path,
                SyncConflictKind.FileDeleteVsModify,
                baseBytes,
                localBytes,
                null));
            return true;
        }

        return false;
    }

    private static void MergeOpaqueFile(
        string path,
        byte[]? baseBytes,
        byte[]? localBytes,
        byte[]? remoteBytes,
        Dictionary<string, byte[]> merged,
        List<SyncConflict> conflicts)
    {
        if (TryMergeFilePresence(path, baseBytes, localBytes, remoteBytes, merged, conflicts))
        {
            return;
        }

        if (baseBytes is null)
        {
            conflicts.Add(FileConflict(path, SyncConflictKind.FileChangedBoth, null, localBytes, remoteBytes));
            return;
        }

        if (JsonEquals(path, localBytes, baseBytes))
        {
            Accept(merged, path, remoteBytes);
            return;
        }

        if (JsonEquals(path, remoteBytes, baseBytes))
        {
            Accept(merged, path, localBytes);
            return;
        }

        conflicts.Add(FileConflict(path, SyncConflictKind.FileChangedBoth, baseBytes, localBytes, remoteBytes));
    }

    private static void AcceptItem(
        Dictionary<string, byte[]> merged,
        string path,
        InventoryItem @base,
        InventoryItem local,
        InventoryItem remote,
        byte[] localBytes,
        DateTimeOffset mergedAt)
    {
        if (ItemUsersEqual(local, @base))
        {
            Accept(merged, path, localBytes);
            return;
        }

        var item = local with
        {
            CreatedAt = Earlier(@base.CreatedAt, local.CreatedAt, remote.CreatedAt),
            UpdatedAt = mergedAt,
            Revision = Math.Max(local.Revision, remote.Revision) + 1,
            DeletedAt = MergeDeletedAt(@base, local, remote),
        };
        ValidateEntity(path, item);
        Accept(merged, path, Serialize(item));
    }

    private static void AcceptPackage(
        Dictionary<string, byte[]> merged,
        string path,
        Package @base,
        Package local,
        Package remote,
        byte[] localBytes,
        DateTimeOffset mergedAt)
    {
        if (PackageUsersEqual(local, @base))
        {
            Accept(merged, path, localBytes);
            return;
        }

        var package = local with
        {
            CreatedAt = Earlier(@base.CreatedAt, local.CreatedAt, remote.CreatedAt),
            UpdatedAt = mergedAt,
            Revision = Math.Max(local.Revision, remote.Revision) + 1,
            DeletedAt = MergeDeletedAt(@base, local, remote),
        };
        ValidateEntity(path, package);
        Accept(merged, path, Serialize(package));
    }

    private static T MergeScalar<T>(
        string path,
        string field,
        T @base,
        T local,
        T remote,
        List<SyncConflict> conflicts)
    {
        if (EqualityComparer<T>.Default.Equals(local, remote))
        {
            return local;
        }

        if (EqualityComparer<T>.Default.Equals(local, @base))
        {
            return remote;
        }

        if (EqualityComparer<T>.Default.Equals(remote, @base))
        {
            return local;
        }

        conflicts.Add(FieldConflict(path, field, SyncConflictKind.FieldChangedBoth, @base, local, remote));
        return local;
    }

    private static DateTimeOffset? MergeDeletedAt(InventoryItem @base, InventoryItem local, InventoryItem remote) =>
        MergeDeletedAt(@base.DeletedAt, local.DeletedAt, remote.DeletedAt);

    private static DateTimeOffset? MergeDeletedAt(Package @base, Package local, Package remote) =>
        MergeDeletedAt(@base.DeletedAt, local.DeletedAt, remote.DeletedAt);

    private static DateTimeOffset? MergeDeletedAt(
        DateTimeOffset? @base,
        DateTimeOffset? local,
        DateTimeOffset? remote)
    {
        if (local is not null && remote is not null)
        {
            return local <= remote ? local : remote;
        }

        if (local == remote)
        {
            return local;
        }

        if (local == @base)
        {
            return remote;
        }

        return local;
    }

    private static IReadOnlyList<string> MergeStringSet(
        IReadOnlyList<string> @base,
        IReadOnlyList<string> local,
        IReadOnlyList<string> remote)
    {
        var baseItems = Unique(Normalize(@base));
        var localItems = Unique(Normalize(local));
        var remoteItems = Unique(Normalize(remote));
        var baseKeys = Keys(baseItems);
        var localKeys = Keys(localItems);
        var remoteKeys = Keys(remoteItems);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in baseItems)
        {
            if (localKeys.Contains(value) && remoteKeys.Contains(value) && seen.Add(value))
            {
                result.Add(value);
            }
        }

        foreach (var value in localItems)
        {
            if (!baseKeys.Contains(value) && seen.Add(value))
            {
                result.Add(value);
            }
        }

        foreach (var value in remoteItems)
        {
            if (!baseKeys.Contains(value) && seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values) =>
        values
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .ToArray();

    private static IReadOnlyList<string> Unique(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static HashSet<string> Keys(IEnumerable<string> values) =>
        new(values, StringComparer.OrdinalIgnoreCase);

    private static bool ItemUsersEqual(InventoryItem left, InventoryItem right) =>
        left.Name == right.Name &&
        left.Category == right.Category &&
        left.Form == right.Form &&
        left.Strength == right.Strength &&
        left.Description == right.Description &&
        left.KeepInStock == right.KeepInStock &&
        left.CoverPhotoId == right.CoverPhotoId &&
        IsDeleted(left) == IsDeleted(right) &&
        SetEquals(left.Aliases, right.Aliases) &&
        SetEquals(left.ActiveIngredients, right.ActiveIngredients);

    private static bool PackageUsersEqual(Package left, Package right) =>
        left.ItemId == right.ItemId &&
        left.Label == right.Label &&
        left.ExpirationDate == right.ExpirationDate &&
        left.ExpirationPrecision == right.ExpirationPrecision &&
        left.OpenedDate == right.OpenedDate &&
        left.ShelfLifeAfterOpeningDays == right.ShelfLifeAfterOpeningDays &&
        left.StockState == right.StockState &&
        left.Note == right.Note &&
        IsDeleted(left) == IsDeleted(right);

    private static bool SetEquals(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        Keys(Normalize(left)).SetEquals(Keys(Normalize(right)));

    private static bool IsDeleted(InventoryItem item) => item.DeletedAt is not null;

    private static bool IsDeleted(Package package) => package.DeletedAt is not null;

    private static Expiration ExpirationOf(Package package) =>
        new(package.ExpirationDate, package.ExpirationPrecision);

    private static bool JsonEquals(string path, byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.AsSpan().SequenceEqual(right))
        {
            return true;
        }

        return JsonNode.DeepEquals(ParseJson(path, left), ParseJson(path, right));
    }

    private static void ValidatePresent(string path, byte[]? content)
    {
        if (content is null)
        {
            return;
        }

        if (path.Equals("aptechka.json", StringComparison.Ordinal))
        {
            ReadManifest(path, content);
            return;
        }

        if (IsEntityPath(path, "items/", out _))
        {
            ReadItem(path, content);
            return;
        }

        if (IsEntityPath(path, "packages/", out _))
        {
            ReadPackage(path, content);
            return;
        }

        ParseJson(path, content);
    }

    private static JsonNode ParseJson(string path, byte[] bytes)
    {
        try
        {
            return JsonNode.Parse(bytes)
                ?? throw new InvalidDataException($"Файл данных пуст: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Некорректный JSON: {path}", exception);
        }
    }

    private static DatasetManifest? ReadManifest(string path, byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

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

    private static InventoryItem? ReadItem(string path, byte[]? bytes) =>
        ReadEntity<InventoryItem>(path, bytes, static (entity, id, entityPath) =>
        {
            if (!string.Equals(entity.Id, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Идентификатор в JSON не совпадает с путём: {entityPath}");
            }

            entity.EnsureValid();
        });

    private static Package? ReadPackage(string path, byte[]? bytes) =>
        ReadEntity<Package>(path, bytes, static (entity, id, entityPath) =>
        {
            if (!string.Equals(entity.Id, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Идентификатор в JSON не совпадает с путём: {entityPath}");
            }

            entity.EnsureValid();
        });

    private static T? ReadEntity<T>(string path, byte[]? bytes, Action<T, string, string> validate)
        where T : class
    {
        if (bytes is null)
        {
            return null;
        }

        if (!IsEntityPath(path, PathPrefix<T>(), out var id))
        {
            throw new InvalidDataException($"Путь не соответствует сущности: {path}");
        }

        try
        {
            var entity = JsonSerializer.Deserialize<T>(bytes, AptechkaJson.Options)
                ?? throw new InvalidDataException($"Файл данных пуст: {path}");
            validate(entity, id, path);
            return entity;
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
            throw new InvalidDataException($"Нарушены инварианты данных: {path}", exception);
        }
    }

    private static string PathPrefix<T>()
    {
        if (typeof(T) == typeof(InventoryItem))
        {
            return "items/";
        }

        if (typeof(T) == typeof(Package))
        {
            return "packages/";
        }

        throw new InvalidOperationException(typeof(T).Name);
    }

    private static bool IsEntityPath(string path, string prefix, out string id)
    {
        id = "";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
            !path.EndsWith(".json", StringComparison.Ordinal))
        {
            return false;
        }

        id = path[prefix.Length..^".json".Length];
        return id.Length > 0 && !id.Contains('/') && UlidGenerator.IsValid(id);
    }

    private static void ValidateEntity(string path, InventoryItem item)
    {
        try
        {
            item.EnsureValid();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Нарушены инварианты данных: {path}", exception);
        }
    }

    private static void ValidateEntity(string path, Package package)
    {
        try
        {
            package.EnsureValid();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Нарушены инварианты данных: {path}", exception);
        }
    }

    private static void Accept(Dictionary<string, byte[]> merged, string path, byte[]? content)
    {
        if (content is null)
        {
            return;
        }

        merged[path] = Copy(content);
    }

    private static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, AptechkaJson.Options);

    private static byte[] Copy(byte[] content)
    {
        var copy = new byte[content.Length];
        Buffer.BlockCopy(content, 0, copy, 0, content.Length);
        return copy;
    }

    private static string? FileJson(byte[]? content) =>
        content is null ? null : Encoding.UTF8.GetString(content);

    private static SyncConflict FileConflict(
        string path,
        SyncConflictKind kind,
        byte[]? @base,
        byte[]? local,
        byte[]? remote) =>
        new(
            $"{path}||{kind}",
            path,
            "",
            kind,
            FileJson(@base),
            FileJson(local),
            FileJson(remote));

    private static SyncConflict FieldConflict<T>(
        string path,
        string field,
        SyncConflictKind kind,
        T @base,
        T local,
        T remote) =>
        new(
            $"{path}|{field}|{kind}",
            path,
            field,
            kind,
            ToJson(@base),
            ToJson(local),
            ToJson(remote));

    private static string ToJson<T>(T value)
    {
        if (value is Expiration expiration)
        {
            return JsonSerializer.Serialize(
                new
                {
                    expirationDate = expiration.Date,
                    expirationPrecision = expiration.Precision,
                },
                ConflictValueOptions);
        }

        return JsonSerializer.Serialize(value, ConflictValueOptions);
    }

    private static JsonSerializerOptions CreateConflictValueOptions()
    {
        var options = new JsonSerializerOptions(AptechkaJson.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
        return options;
    }

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second, DateTimeOffset third)
    {
        var earliest = first <= second ? first : second;
        return earliest <= third ? earliest : third;
    }

    private static int CompareConflicts(SyncConflict left, SyncConflict right)
    {
        var path = string.CompareOrdinal(left.Path, right.Path);
        if (path != 0)
        {
            return path;
        }

        var field = string.CompareOrdinal(left.Field, right.Field);
        if (field != 0)
        {
            return field;
        }

        return left.Kind.CompareTo(right.Kind);
    }

    private readonly record struct Expiration(DateOnly? Date, ExpirationPrecision? Precision);
}
