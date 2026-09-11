using System.Net;
using System.Text;
using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

public sealed class SyncFailureAndInspectionTests
{
    private const string Token = "github_pat_must-not-leak";
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string DatasetId = "01ARZ3NDEKTSV4RRFFQ69G5FAZ";
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 5, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(401, false, SyncFailureKind.Authentication)]
    [InlineData(403, false, SyncFailureKind.AccessDenied)]
    [InlineData(403, true, SyncFailureKind.RateLimited)]
    [InlineData(429, false, SyncFailureKind.RateLimited)]
    [InlineData(404, false, SyncFailureKind.RepositoryNotFound)]
    [InlineData(500, false, SyncFailureKind.RemoteUnavailable)]
    [InlineData(503, false, SyncFailureKind.RemoteUnavailable)]
    public async Task Sync_ClassifiesGitHubStatus(int statusCode, bool rateLimited, SyncFailureKind expected)
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, new GitHubRemoteSnapshot("sha-0", "tree", snapshot));
        harness.GitHub.GetErrors.Enqueue(new GitHubApiException(statusCode, "raw github body", rateLimited));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(expected, exception.Kind);
        AssertSafe(exception);
        Assert.NotNull(await harness.StateStore.LoadAsync());
    }

    [Fact]
    public async Task Sync_ClassifiesOffline()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        harness.GitHub.GetErrors.Enqueue(new HttpRequestException("offline"));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.Offline, exception.Kind);
        AssertSafe(exception);
    }

    [Fact]
    public async Task Sync_ClassifiesTimeoutWithoutTreatingUserCancellationAsFailure()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        harness.GitHub.GetErrors.Enqueue(new TimeoutException("timeout"));

        var timeout = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());
        Assert.Equal(SyncFailureKind.Timeout, timeout.Kind);
        AssertSafe(timeout);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Sync(cts.Token));
    }

    [Fact]
    public async Task Sync_ClassifiesInvalidConfiguration()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() =>
            harness.Service.SyncAsync(new SyncTarget("bad owner", "repo", "main"), Token, "device"));

        Assert.Equal(SyncFailureKind.InvalidConfiguration, exception.Kind);
        AssertSafe(exception);
    }

    [Fact]
    public async Task Sync_ClassifiesInvalidJsonAsInvalidData()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        harness.GitHub.GetErrors.Enqueue(new JsonException("{\"name\":\"leak\"}"));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.InvalidData, exception.Kind);
        AssertSafe(exception);
    }

    [Fact]
    public async Task Sync_ClassifiesLocalIo()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        harness.Store.ReadError = new IOException(@"C:\secret\data\items.json");

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.LocalStorage, exception.Kind);
        AssertSafe(exception);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sync_ClassifiesThreeCasMisses()
    {
        var item = Item();
        var local = Snapshot(item.Update(Now, item.Name, item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock));
        var remote = Snapshot(Rename(item, "Ибуфен"));
        await using var harness = await Harness.CreateAsync(Snapshot(item), local, Remote(remote));
        harness.Store.AlwaysMissReplace = true;

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.LocalChangedRepeatedly, exception.Kind);
        AssertSafe(exception);
        Assert.Equal(3, harness.Store.TryReplaceCalls);
    }

    [Fact]
    public async Task Sync_ClassifiesThreeHeadRaces()
    {
        var item = Item();
        var renamed = Rename(item, "Нурофен");
        var @base = Snapshot(item);
        var local = Snapshot(renamed);
        await using var harness = await Harness.CreateAsync(@base, local, Remote(@base));
        for (var i = 0; i < 3; i++)
        {
            harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        }

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.RemoteChangedRepeatedly, exception.Kind);
        AssertSafe(exception);
    }

    [Fact]
    public async Task Sync_ClassifiesUnknownWithoutLeakingDetails()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        harness.GitHub.GetErrors.Enqueue(new InvalidOperationException("github_pat_must-not-leak {\"raw\":true}"));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.Unknown, exception.Kind);
        AssertSafe(exception);
    }

    [Fact]
    public async Task State_ReadsLegacyJsonWithoutNewFields()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "sync", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var snapshot = Snapshot();
        var legacy = new
        {
            datasetId = DatasetId,
            lastCommitSha = "sha-old",
            baseFiles = snapshot.Files.ToDictionary(
                static pair => pair.Key,
                static pair => Convert.ToBase64String(pair.Value)),
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy, AptechkaJson.Options));
        var store = new SyncStateStore(path, new StubClock(Now));

        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Null(loaded.LastSuccessfulAt);
        Assert.Null(loaded.LastSuccessfulOutcome);
        Assert.True(loaded.GetBaseSnapshot().HasSameFiles(snapshot));
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task State_SuccessfulOutcomeRecordsClockAndConflictDoesNotMoveIt()
    {
        var item = Item();
        var renamed = Rename(item, "Нурофен");
        var @base = Snapshot(item);
        var local = Snapshot(renamed);
        var remote = Snapshot(Rename(item, "Ибуфен"));
        await using var harness = await Harness.CreateAsync(@base, local, Remote(remote));
        var conflict = Assert.Single((await harness.Sync()).Conflicts);
        var before = await harness.StateStore.LoadAsync();

        await harness.Resolve([new SyncConflictResolution(conflict, SyncConflictSide.Local)]);
        var afterSuccess = await harness.StateStore.LoadAsync();

        Assert.Equal(Now, afterSuccess!.LastSuccessfulAt);
        Assert.Equal(SyncOutcome.Pushed, afterSuccess.LastSuccessfulOutcome);

        harness.GitHub.GetErrors.Enqueue(new HttpRequestException("offline"));
        await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());
        var afterError = await harness.StateStore.LoadAsync();
        Assert.Equal(afterSuccess.LastSuccessfulAt, afterError!.LastSuccessfulAt);
        Assert.Equal(afterSuccess.LastSuccessfulOutcome, afterError.LastSuccessfulOutcome);
        Assert.True(afterError.GetBaseSnapshot().HasSameFiles(afterSuccess.GetBaseSnapshot()));
        Assert.Equal(Now, before!.LastSuccessfulAt);
    }

    [Fact]
    public async Task Inspector_DistinguishesSyncedLocalChangesAndMissingBaseWithoutWriting()
    {
        var item = Item();
        var @base = Snapshot(item);
        await using var synced = await Harness.CreateAsync(@base, @base, Remote(@base));
        var written = File.GetLastWriteTimeUtc(harnessState(synced));

        var matching = await synced.Inspector.InspectAsync();
        Assert.Equal(SyncInspectionCondition.MatchesBase, matching.Condition);
        Assert.Equal(Now, matching.LastSuccessfulAt);
        Assert.Equal(File.GetLastWriteTimeUtc(harnessState(synced)), written);

        await synced.Repository.ReplaceAsync(Snapshot(Rename(item, "другое")));
        var changed = await synced.Inspector.InspectAsync();
        Assert.Equal(SyncInspectionCondition.LocalChanges, changed.Condition);
        Assert.Equal(File.GetLastWriteTimeUtc(harnessState(synced)), written);

        await using var missing = await Harness.CreateAsync(null, @base, Remote(@base));
        var none = await missing.Inspector.InspectAsync();
        Assert.Equal(SyncInspectionCondition.NoSuccessfulSync, none.Condition);
        Assert.False(File.Exists(harnessState(missing)));
    }

    [Fact]
    public async Task Inspector_DoesNotTreatCorruptStateAsSynced()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        await File.WriteAllTextAsync(harnessState(harness), """{"datasetId":"x","lastCommitSha":"sha","baseFiles":{"aptechka.json":"@@"}}""");

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Inspector.InspectAsync());

        Assert.Equal(SyncFailureKind.InvalidData, exception.Kind);
        AssertSafe(exception);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"lastCommitSha":"sha","baseFiles":{}}""")]
    [InlineData("""{"datasetId":"01ARZ3NDEKTSV4RRFFQ69G5FAZ","lastCommitSha":"sha","baseFiles":null}""")]
    [InlineData("""{"datasetId":"01ARZ3NDEKTSV4RRFFQ69G5FAZ","lastCommitSha":"sha"}""")]
    [InlineData("""{"datasetId":"","lastCommitSha":"sha","baseFiles":{}}""")]
    [InlineData("""{"datasetId":"01ARZ3NDEKTSV4RRFFQ69G5FAZ","lastCommitSha":"","baseFiles":{}}""")]
    public async Task Inspector_ClassifiesCorruptStateAsInvalidData(string json)
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        await File.WriteAllTextAsync(harnessState(harness), json);

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Inspector.InspectAsync());

        Assert.Equal(SyncFailureKind.InvalidData, exception.Kind);
        AssertSafe(exception);
        Assert.DoesNotContain(harness.StatePath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspector_ClassifiesUnreadableStateAsLocalStorage()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        await using var locked = new FileStream(harnessState(harness), FileMode.Open, FileAccess.Read, FileShare.None);

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Inspector.InspectAsync());

        Assert.Equal(SyncFailureKind.LocalStorage, exception.Kind);
        AssertSafe(exception);
        Assert.DoesNotContain(harness.StatePath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspector_PropagatesCallerCancellation()
    {
        var snapshot = Snapshot();
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote(snapshot));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Inspector.InspectAsync(cts.Token));
    }

    [Fact]
    public async Task Sync_LinkedCallerCancellationIsNotTimeout()
    {
        var snapshot = Snapshot();
        var handler = new HoldingHandler();
        await using var session = await LinkedClientSession.CreateAsync(snapshot, handler);
        using var cts = new CancellationTokenSource();

        var sync = session.Service.SyncAsync(new SyncTarget("owner", "repo", "main"), Token, "device", cts.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync);
        Assert.IsNotType<SyncFailureException>(exception);
        Assert.NotEqual(cts.Token, handler.ObservedToken);
        Assert.True(handler.ObservedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Sync_LinkedTimeoutWithoutCallerCancellationIsTimeout()
    {
        var snapshot = Snapshot();
        var handler = new HoldingHandler { CancelLinkedTokenImmediately = true };
        await using var session = await LinkedClientSession.CreateAsync(snapshot, handler);

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() =>
            session.Service.SyncAsync(new SyncTarget("owner", "repo", "main"), Token, "device"));

        Assert.Equal(SyncFailureKind.Timeout, exception.Kind);
        AssertSafe(exception);
        Assert.True(handler.ObservedToken.CanBeCanceled);
        Assert.False(handler.ObservedToken.IsCancellationRequested);
    }

    private static void AssertSafe(SyncFailureException exception)
    {
        Assert.DoesNotContain(Token, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("github_pat", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string harnessState(Harness harness) => harness.StatePath;

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "aptechka-sync-tests", Guid.NewGuid().ToString("N"));

    private static InventoryItem Item() =>
        InventoryItem.Create(ItemId, Now, "Ибупрофен", [], InventoryItemCategory.Medicine, ["ибупрофен"], "таблетки", "200 мг", null, true);

    private static InventoryItem Rename(InventoryItem item, string name) =>
        item.Update(Now, name, item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

    private static DataSnapshot Snapshot(InventoryItem? item = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["aptechka.json"] = JsonSerializer.SerializeToUtf8Bytes(
                new DatasetManifest(DatasetManifest.CurrentSchemaVersion, DatasetId, Now),
                AptechkaJson.Options),
        };
        if (item is not null)
        {
            files[$"items/{item.Id}.json"] = JsonSerializer.SerializeToUtf8Bytes(item, AptechkaJson.Options);
        }

        return new DataSnapshot(files);
    }

    private static GitHubRemoteSnapshot Remote(DataSnapshot data) => new("sha-0", "tree-sha", data);

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            string rootPath,
            FileInventoryRepository repository,
            FaultingSnapshotStore store,
            SyncStateStore stateStore,
            FakeGitHub gitHub,
            GitHubSyncService service,
            SyncStateInspector inspector)
        {
            StatePath = Path.Combine(rootPath, "sync", "state.json");
            Repository = repository;
            Store = store;
            StateStore = stateStore;
            GitHub = gitHub;
            Service = service;
            Inspector = inspector;
        }

        public string StatePath { get; }
        public FileInventoryRepository Repository { get; }
        public FaultingSnapshotStore Store { get; }
        public SyncStateStore StateStore { get; }
        public FakeGitHub GitHub { get; }
        public GitHubSyncService Service { get; }
        public SyncStateInspector Inspector { get; }

        public static async Task<Harness> CreateAsync(
            DataSnapshot? @base,
            DataSnapshot local,
            GitHubRemoteSnapshot remote)
        {
            var root = NewRoot();
            var clock = new StubClock(Now);
            var repository = new FileInventoryRepository(Path.Combine(root, "data"), clock, new SequenceIdGenerator());
            if (!local.IsEmpty)
            {
                await repository.ReplaceAsync(local);
            }

            var store = new FaultingSnapshotStore(repository);
            var stateStore = new SyncStateStore(Path.Combine(root, "sync", "state.json"), clock);
            if (@base is not null)
            {
                await stateStore.SaveAsync(DatasetId, remote.CommitSha, @base, SyncOutcome.Pulled);
            }

            var gitHub = new FakeGitHub { Remote = remote };
            var service = new GitHubSyncService(store, stateStore, gitHub, clock);
            return new Harness(root, repository, store, stateStore, gitHub, service, new SyncStateInspector(store, stateStore));
        }

        public Task<SyncResult> Sync(CancellationToken cancellationToken = default) =>
            Service.SyncAsync(new SyncTarget("owner", "repo", "main"), Token, "device", cancellationToken);

        public Task<SyncResult> Resolve(IReadOnlyList<SyncConflictResolution> resolutions) =>
            Service.ResolveConflictsAsync(new SyncTarget("owner", "repo", "main"), Token, "device", resolutions);

        public ValueTask DisposeAsync()
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(StatePath));
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FaultingSnapshotStore(IDataSnapshotStore inner) : IDataSnapshotStore
    {
        public int TryReplaceCalls { get; private set; }
        public IOException? ReadError { get; set; }
        public bool AlwaysMissReplace { get; set; }

        public Task<DataSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            ReadError is not null ? throw ReadError : inner.ReadAsync(cancellationToken);

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) =>
            inner.EnsureInitializedAsync(cancellationToken);

        public Task ReplaceAsync(DataSnapshot snapshot, CancellationToken cancellationToken = default) =>
            inner.ReplaceAsync(snapshot, cancellationToken);

        public Task<bool> TryReplaceAsync(DataSnapshot expected, DataSnapshot replacement, CancellationToken cancellationToken = default)
        {
            TryReplaceCalls++;
            return AlwaysMissReplace ? Task.FromResult(false) : inner.TryReplaceAsync(expected, replacement, cancellationToken);
        }
    }

    private sealed class FakeGitHub : IGitHubDataClient
    {
        public GitHubRemoteSnapshot? Remote { get; set; }
        public Queue<Exception> GetErrors { get; } = [];
        public Queue<Exception> CommitErrors { get; } = [];

        public Task<GitHubRemoteSnapshot?> GetSnapshotAsync(SyncTarget target, string accessToken, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetErrors.Count > 0)
            {
                throw GetErrors.Dequeue();
            }

            return Task.FromResult(Remote);
        }

        public Task InitializeRepositoryAsync(SyncTarget target, string accessToken, byte[] manifestContent, string deviceName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> CommitSnapshotAsync(SyncTarget target, string accessToken, GitHubRemoteSnapshot remote, DataSnapshot local, string deviceName, CancellationToken cancellationToken = default)
        {
            if (CommitErrors.Count > 0)
            {
                throw CommitErrors.Dequeue();
            }

            Remote = new GitHubRemoteSnapshot("commit-1", "tree-commit-1", local);
            return Task.FromResult("commit-1");
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
        public DateOnly Today => DateOnly.FromDateTime(utcNow.Date);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public string Create(DateTimeOffset timestamp) => DatasetId;
    }

    private sealed class LinkedClientSession : IAsyncDisposable
    {
        private readonly string root;
        private readonly HttpClient http;

        private LinkedClientSession(string root, HttpClient http, GitHubSyncService service)
        {
            this.root = root;
            this.http = http;
            Service = service;
        }

        public GitHubSyncService Service { get; }

        public static async Task<LinkedClientSession> CreateAsync(DataSnapshot snapshot, HoldingHandler handler)
        {
            var root = NewRoot();
            var clock = new StubClock(Now);
            var repository = new FileInventoryRepository(Path.Combine(root, "data"), clock, new SequenceIdGenerator());
            if (!snapshot.IsEmpty)
            {
                await repository.ReplaceAsync(snapshot);
            }

            var http = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.github.com/"),
            };
            var service = new GitHubSyncService(
                new FaultingSnapshotStore(repository),
                new SyncStateStore(Path.Combine(root, "sync", "state.json"), clock),
                new GitHubDataClient(http),
                clock);
            return new LinkedClientSession(root, http, service);
        }

        public ValueTask DisposeAsync()
        {
            http.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class HoldingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        public bool CancelLinkedTokenImmediately { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            Entered.TrySetResult();
            if (CancelLinkedTokenImmediately)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
