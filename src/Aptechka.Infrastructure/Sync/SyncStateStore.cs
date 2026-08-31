using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Infrastructure.Sync;

public sealed class SyncStateStore(string statePath)
{
    public async Task<SyncState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(statePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<SyncState>(
            stream,
            AptechkaJson.Options,
            cancellationToken);
    }

    public async Task SaveAsync(
        string datasetId,
        string commitSha,
        DataSnapshot baseSnapshot,
        CancellationToken cancellationToken = default)
    {
        var state = new SyncState(
            datasetId,
            commitSha,
            baseSnapshot.Files.ToDictionary(
                static pair => pair.Key,
                static pair => Convert.ToBase64String(pair.Value),
                StringComparer.Ordinal));

        var directory = Path.GetDirectoryName(statePath)
            ?? throw new InvalidOperationException("У sync state отсутствует родительский каталог.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{statePath}.{Guid.NewGuid():N}.tmp";
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
                    state,
                    AptechkaJson.Options,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, statePath, true);
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

public sealed record SyncState(
    string DatasetId,
    string LastCommitSha,
    IReadOnlyDictionary<string, string> BaseFiles)
{
    public DataSnapshot GetBaseSnapshot() => new(
        BaseFiles.ToDictionary(
            static pair => pair.Key,
            static pair => Convert.FromBase64String(pair.Value),
            StringComparer.Ordinal));
}
