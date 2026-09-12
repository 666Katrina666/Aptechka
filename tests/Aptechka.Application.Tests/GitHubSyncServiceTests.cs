using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

public sealed class GitHubSyncServiceTests
{
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string PackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string DatasetId = "01ARZ3NDEKTSV4RRFFQ69G5FAZ";
    private const string OtherDatasetId = "01ARZ3NDEKTSV4RRFFQ69G5FB0";
    private const string Token = "test-token";

    private static readonly DateTimeOffset T0 = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly DateTimeOffset MergedAt = T0.AddHours(3);

    [Fact]
    public async Task Sync_MergesDifferentFilesAndPushes()
    {
        var item = Item(ItemId, "Ибупрофен");
        var other = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var localItem = Rename(item, "Нурофен", T1);
        var remoteOther = Rename(other, "Вата стерильная", T2);
        var @base = Snap(File(Manifest()), File(item), File(other));
        var local = Snap(File(Manifest()), File(localItem), File(other));
        var remote = Snap(File(Manifest()), File(item), File(remoteOther));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));

        var result = await harness.Sync();
        var expected = Merge(@base, local, remote);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-0"], harness.GitHub.CommitParents);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-1", expected);
        Assert.DoesNotContain("Нурофен", result.Message);
        Assert.DoesNotContain(Token, result.Message);
    }

    [Fact]
    public async Task Sync_MergesDifferentFieldsOfOneItemAndPushes()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remoteItem = item.Update(T2, item.Name, item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));

        var result = await harness.Sync();
        var expected = Merge(@base, local, remote);
        var mergedItem = ReadItem(expected, ItemId);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-0"], harness.GitHub.CommitParents);
        Assert.Equal("Нурофен", mergedItem.Name);
        Assert.Equal("капсулы", mergedItem.Form);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task Sync_MergesPackageFieldsAndPushes()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var localPackage = package.Update(T1, "блистер", package.ExpirationDate, package.ExpirationPrecision, package.OpenedDate, package.ShelfLifeAfterOpeningDays, package.StockState, package.Note);
        var remotePackage = package.Update(T2, package.Label, package.ExpirationDate, package.ExpirationPrecision, package.OpenedDate, package.ShelfLifeAfterOpeningDays, StockState.Low, package.Note);
        var @base = Snap(File(Manifest()), File(item), File(package));
        var local = Snap(File(Manifest()), File(item), File(localPackage));
        var remote = Snap(File(Manifest()), File(item), File(remotePackage));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));

        var result = await harness.Sync();
        var expected = Merge(@base, local, remote);
        var mergedPackage = ReadPackage(expected, PackageId);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal("блистер", mergedPackage.Label);
        Assert.Equal(StockState.Low, mergedPackage.StockState);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task Sync_KeepsShoppingWhenMergingAndPushing()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var shopping = ShoppingItem.Create(ItemId, T0, true, "аптека");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item), File(package), File(shopping));
        var local = Snap(File(Manifest()), File(localItem), File(package), File(shopping));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));

        var result = await harness.Sync();
        var expected = Merge(@base, local, @base);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.True(expected.Files.ContainsKey($"items/{ItemId}.json"));
        Assert.True(expected.Files.ContainsKey($"packages/{PackageId}.json"));
        Assert.True(expected.Files.ContainsKey($"shopping/{ItemId}.json"));
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        Assert.True(ReadShopping(expected, ItemId).IsRequested);
        Assert.Equal("аптека", ReadShopping(expected, ItemId).Note);
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task Sync_PullsWhenMergedEqualsRemoteWithoutCommit()
    {
        var item = Item(ItemId, "Ибупрофен");
        var remoteItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, @base, Remote("sha-r", remote));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Pulled, result.Outcome);
        Assert.Equal("sha-r", result.CommitSha);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(remote));
        await AssertStateAsync(harness, "sha-r", remote);
    }

    [Fact]
    public async Task Sync_ReturnsUpToDateWithoutCommitWhenLocalEqualsRemote()
    {
        var snapshot = Snap(File(Manifest()), File(Item(ItemId, "Ибупрофен")));
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote("sha-r", snapshot));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.UpToDate, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(snapshot));
        await AssertStateAsync(harness, "sha-r", snapshot);
    }

    [Fact]
    public async Task Sync_DoesNotCreateEmptyCommitWhenBothSidesMadeTheSameChange()
    {
        var item = Item(ItemId, "Ибупрофен");
        var changed = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var both = Snap(File(Manifest()), File(changed));
        await using var harness = await Harness.CreateAsync(@base, both, Remote("sha-r", both));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.UpToDate, result.Outcome);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(both));
        await AssertStateAsync(harness, "sha-r", both);
    }

    [Fact]
    public async Task Sync_FieldConflictLeavesLocalRemoteAndBaseUnchanged()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Null(result.CommitSha);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.FieldChangedBoth, conflict.Kind);
        Assert.Equal("name", conflict.Field);
        Assert.DoesNotContain("Нурофен", result.Message);
        Assert.DoesNotContain("Ибуфен", result.Message);
        Assert.DoesNotContain(conflict.LocalValueJson!, result.Message);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task Sync_TombstoneVersusModificationIsConflict()
    {
        var item = Item(ItemId, "Ибупрофен");
        var deleted = item.Delete(T1);
        var changed = Rename(item, "Нурофен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(deleted));
        var remote = Snap(File(Manifest()), File(changed));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Equal(SyncConflictKind.DeleteVsModify, Assert.Single(result.Conflicts).Kind);
        Assert.DoesNotContain("Нурофен", result.Message);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task Sync_RetriesPushAfterOneHeadRace()
    {
        var item = Item(ItemId, "Ибупрофен");
        var other = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var firstRemote = Snap(File(Manifest()), File(item));
        var secondRemote = Snap(File(Manifest()), File(item), File(other));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", firstRemote));
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-1", secondRemote));
        var expected = Merge(@base, local, secondRemote);

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(2, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(2, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-0", "sha-1"], harness.GitHub.CommitParents);
        Assert.True(harness.GitHub.Committed[1].HasSameFiles(expected));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-2", expected);
        Assert.Equal("Нурофен", ReadItem(expected, ItemId).Name);
        Assert.Equal("Вата", ReadItem(expected, SecondItemId).Name);
    }

    [Fact]
    public async Task Sync_DoesNotAdvanceBaseAfterThreeHeadRaces()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-1", @base));
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-2", @base));
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-3", @base));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.RemoteChangedRepeatedly, exception.Kind);
        Assert.DoesNotContain(Token, exception.Message);
        Assert.DoesNotContain("Нурофен", exception.Message);
        Assert.Equal(3, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(3, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-0", "sha-1", "sha-2"], harness.GitHub.CommitParents);
        Assert.Equal("Нурофен", ReadItem(await harness.Repository.ReadAsync(), ItemId).Name);
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task Sync_DoesNotRetryOrdinaryGitHubErrorAsHeadRace()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        harness.GitHub.CommitErrors.Enqueue(new GitHubApiException(401, "Unauthorized"));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.Authentication, exception.Kind);
        Assert.DoesNotContain(Token, exception.Message);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task Sync_RetriesMergeWhenCasFailsOnceAndKeepsLocalEdit()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remoteItem = item.Update(T2, item.Name, item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var editedLocalItem = localItem.Update(T1.AddMinutes(5), localItem.Name, localItem.Aliases, localItem.Category, localItem.ActiveIngredients, localItem.Form, localItem.Strength, "во время sync", localItem.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        var editedLocal = Snap(File(Manifest()), File(editedLocalItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        harness.Store.BeforeTryReplace = async (call, _, _) =>
        {
            if (call == 1)
            {
                await harness.Repository.ReplaceAsync(editedLocal);
            }
        };

        var result = await harness.Sync();
        var expected = Merge(@base, editedLocal, remote);
        var mergedItem = ReadItem(expected, ItemId);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(2, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal(2, harness.Store.TryReplaceCalls);
        Assert.Equal("Нурофен", mergedItem.Name);
        Assert.Equal("капсулы", mergedItem.Form);
        Assert.Equal("во время sync", mergedItem.Description);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task Sync_KeepsPostCasLocalEditAndSavesPushedSnapshotAsBase()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        harness.GitHub.BeforeCommit = async (_, snapshot, _) =>
        {
            var current = ReadItem(snapshot, ItemId);
            await harness.Repository.SaveItemAsync(
                current.Update(
                    T2,
                    "после CAS",
                    current.Aliases,
                    current.Category,
                    current.ActiveIngredients,
                    current.Form,
                    current.Strength,
                    current.Description,
                    current.KeepInStock));
        };

        var result = await harness.Sync();
        var expected = Merge(@base, local, @base);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        Assert.Equal("Нурофен", ReadItem(harness.GitHub.Committed[0], ItemId).Name);
        Assert.Equal("после CAS", ReadItem(await harness.Repository.ReadAsync(), ItemId).Name);
        await AssertStateAsync(harness, "commit-1", expected);
        Assert.Equal("Нурофен", ReadItem((await harness.StateStore.LoadAsync())!.GetBaseSnapshot(), ItemId).Name);
    }

    [Fact]
    public async Task Sync_KeepsMergedLocalAndDoesNotSaveStateWhenCommitFails()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        harness.GitHub.CommitErrors.Enqueue(new GitHubApiException(500, "GitHub unavailable"));

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());
        var expected = Merge(@base, local, @base);

        Assert.Equal(SyncFailureKind.RemoteUnavailable, exception.Kind);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task Sync_NoBaseCompatibleSubsetPushes()
    {
        var item = Item(ItemId, "Ибупрофен");
        var remote = Snap(File(Manifest()));
        var local = Snap(File(Manifest()), File(item));
        await using var harness = await Harness.CreateAsync(null, local, Remote("sha-0", remote));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-0"], harness.GitHub.CommitParents);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(local));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "commit-1", local);
    }

    [Fact]
    public async Task Sync_NoBaseIncompatibleSnapshotsConflictWithoutWritingState()
    {
        var local = Snap(File(Manifest()), File(Item(ItemId, "Ибупрофен")));
        var remote = Snap(File(Manifest()), File(Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply)));
        await using var harness = await Harness.CreateAsync(null, local, Remote("sha-0", remote));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        Assert.Null(await harness.StateStore.LoadAsync());
    }

    [Fact]
    public async Task Sync_DatasetMismatchDoesNotChangeState()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = Snap(File(Manifest()), File(item));
        var remote = Snap(File(new DatasetManifest(DatasetManifest.CurrentSchemaVersion, OtherDatasetId, T0)), File(item));
        await using var harness = await Harness.CreateAsync(local, local, Remote("sha-0", remote));

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Contains("datasetId", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "sha-0", local);
    }

    [Fact]
    public async Task Sync_HonorsCancellationToken()
    {
        var snapshot = Snap(File(Manifest()), File(Item(ItemId, "Ибупрофен")));
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote("sha-0", snapshot));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Sync(cts.Token));

        Assert.Equal(0, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        await AssertStateAsync(harness, "sha-0", snapshot);
    }

    [Fact]
    public async Task Sync_HeadRaceUsesObservedRemoteAsEphemeralBaseForSameFieldChange()
    {
        var item = Item(ItemId, "A");
        var localItem = Describe(item, "local", T1);
        var remote1Item = Rename(item, "B", T1);
        var remote2Item = Rename(remote1Item, "C", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote1 = Snap(File(Manifest()), File(remote1Item));
        var remote2 = Snap(File(Manifest()), File(remote2Item));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-r1", remote1));
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-r2", remote2));
        var firstMerged = Merge(@base, local, remote1);
        var expected = Merge(remote1, firstMerged, remote2);

        var result = await harness.Sync();
        var pushed = ReadItem(harness.GitHub.Committed[1], ItemId);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(["sha-r1", "sha-r2"], harness.GitHub.CommitParents);
        Assert.Equal("C", pushed.Name);
        Assert.Equal("local", pushed.Description);
        Assert.True(harness.GitHub.Committed[1].HasSameFiles(expected));
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-2", expected);
    }

    [Fact]
    public async Task Sync_NoBaseHeadRaceMergesAgainstObservedRemoteInsteadOfConflicting()
    {
        var localItem = Item(ItemId, "Ибупрофен");
        var remoteItem = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var remote1 = Snap(File(Manifest()));
        var local = Snap(File(Manifest()), File(localItem));
        var remote2 = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(null, local, Remote("sha-r1", remote1));
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-r2", remote2));
        var expected = Merge(remote1, local, remote2);

        var result = await harness.Sync();

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(2, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-r1", "sha-r2"], harness.GitHub.CommitParents);
        Assert.True(harness.GitHub.Committed[1].HasSameFiles(expected));
        Assert.True(expected.Files.ContainsKey($"items/{ItemId}.json"));
        Assert.True(expected.Files.ContainsKey($"items/{SecondItemId}.json"));
        await AssertStateAsync(harness, "commit-2", expected);
    }

    [Fact]
    public async Task Sync_HeadRaceChainAdvancesEphemeralBaseEachAttempt()
    {
        var item = Item(ItemId, "A");
        var localItem = Describe(item, "local", T1);
        var remote1Item = Rename(item, "B", T1);
        var remote2Item = Rename(remote1Item, "C", T2);
        var remote3Item = Rename(remote2Item, "D", T2.AddHours(1));
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote1 = Snap(File(Manifest()), File(remote1Item));
        var remote2 = Snap(File(Manifest()), File(remote2Item));
        var remote3 = Snap(File(Manifest()), File(remote3Item));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-r1", remote1));
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-r2", remote2));
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-r3", remote3));
        var merged1 = Merge(@base, local, remote1);
        var merged2 = Merge(remote1, merged1, remote2);
        var expected = Merge(remote2, merged2, remote3);

        var result = await harness.Sync();
        var pushed = ReadItem(harness.GitHub.Committed[2], ItemId);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(3, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-r1", "sha-r2", "sha-r3"], harness.GitHub.CommitParents);
        Assert.Equal("D", pushed.Name);
        Assert.Equal("local", pushed.Description);
        Assert.True(harness.GitHub.Committed[2].HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-3", expected);
    }

    [Fact]
    public async Task Sync_SerializesConcurrentCallsAndKeepsGateAfterCanceledWaiter()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.GitHub.BlockGetSnapshot = hold.Task;

        var firstTask = harness.Sync();
        await harness.GitHub.EnteredGetSnapshot.Task;
        using var secondCts = new CancellationTokenSource();
        var secondTask = harness.Sync(secondCts.Token);
        await Task.Delay(150);

        Assert.False(firstTask.IsCompleted);
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.MaxGitHubInFlight);

        secondCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);
        Assert.False(firstTask.IsCompleted);

        hold.SetResult();
        var first = await firstTask;
        var expected = Merge(@base, local, @base);

        Assert.Equal(SyncOutcome.Pushed, first.Outcome);
        Assert.Equal(1, harness.GitHub.MaxGitHubInFlight);
        await AssertStateAsync(harness, "commit-1", expected);

        var third = await harness.Sync();
        Assert.Equal(SyncOutcome.UpToDate, third.Outcome);
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task Sync_SecondCallWaitsForFirstThenCompletesWithoutOverlappingGitHub()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.GitHub.BlockGetSnapshot = hold.Task;

        var firstTask = harness.Sync();
        await harness.GitHub.EnteredGetSnapshot.Task;
        var secondTask = harness.Sync();
        await Task.Delay(150);

        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.Equal(1, harness.GitHub.MaxGitHubInFlight);

        hold.SetResult();
        var first = await firstTask;
        var second = await secondTask;
        var expected = Merge(@base, local, @base);

        Assert.Equal(SyncOutcome.Pushed, first.Outcome);
        Assert.Equal(SyncOutcome.UpToDate, second.Outcome);
        Assert.Equal(2, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal(1, harness.GitHub.MaxGitHubInFlight);
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task ResolveConflicts_FullyResolvesAndPushes()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        var conflict = Assert.Single((await harness.Sync()).Conflicts);
        var expected = Merge(@base, local, remote, new SyncConflictResolution(conflict, SyncConflictSide.Local));

        var result = await harness.Resolve([new SyncConflictResolution(conflict, SyncConflictSide.Local)]);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Empty(result.Conflicts);
        Assert.Equal(["sha-0"], harness.GitHub.CommitParents);
        Assert.Equal("Нурофен", ReadItem(harness.GitHub.Committed[0], ItemId).Name);
        Assert.DoesNotContain("Нурофен", result.Message);
        Assert.DoesNotContain(Token, result.Message);
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task ResolveConflicts_PartialResolutionDoesNotChangeState()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var remoteItem = item.Update(T2, "Ибуфен", item.Aliases, item.Category, item.ActiveIngredients, "сироп", item.Strength, item.Description, item.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        var nameConflict = (await harness.Sync()).Conflicts.Single(static conflict => conflict.Field == "name");

        var result = await harness.Resolve([new SyncConflictResolution(nameConflict, SyncConflictSide.Local)]);

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Equal("form", Assert.Single(result.Conflicts).Field);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task Sync_ThreeCasMissesBecomeTypedFailure()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, item.Name, item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        harness.Store.AlwaysMissReplace = true;

        var exception = await Assert.ThrowsAsync<SyncFailureException>(() => harness.Sync());

        Assert.Equal(SyncFailureKind.LocalChangedRepeatedly, exception.Kind);
        Assert.Equal(3, harness.Store.TryReplaceCalls);
        Assert.DoesNotContain("Нурофен", exception.Message);
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task ResolveConflicts_DoesNotApplyChoiceWhenRemoteChanged()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var laterRemote = Rename(item, "Цитрамон", T2.AddMinutes(1));
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        var conflict = Assert.Single((await harness.Sync()).Conflicts);
        harness.GitHub.Remote = Remote("sha-1", Snap(File(Manifest()), File(laterRemote)));

        var result = await harness.Resolve([new SyncConflictResolution(conflict, SyncConflictSide.Local)]);

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Equal("\"Цитрамон\"", Assert.Single(result.Conflicts).RemoteValueJson);
        Assert.DoesNotContain("Цитрамон", result.Message);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task ResolveConflicts_DoesNotApplyStaleDeleteChoiceWhenRemoteEntityChanges()
    {
        var item = Item(ItemId, "Ибупрофен");
        var deleted = item.Delete(T1);
        var remoteB = Rename(item, "B", T2);
        var remoteC = Rename(item, "C", T2.AddMinutes(1));
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(deleted));
        var remote = Snap(File(Manifest()), File(remoteB));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        var conflict = Assert.Single((await harness.Sync()).Conflicts);
        Assert.Equal(SyncConflictKind.DeleteVsModify, conflict.Kind);
        harness.GitHub.Remote = Remote("sha-1", Snap(File(Manifest()), File(remoteC)));

        var result = await harness.Resolve([new SyncConflictResolution(conflict, SyncConflictSide.Remote)]);
        var returned = Assert.Single(result.Conflicts);

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.NotEqual(conflict.RemoteValueJson, returned.RemoteValueJson);
        Assert.Contains("\"name\":\"C\"", returned.RemoteValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("B", result.Message);
        Assert.DoesNotContain("C", result.Message);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        Assert.Equal(0, harness.Store.TryReplaceCalls);
        Assert.True((await harness.Repository.ReadAsync()).HasSameFiles(local));
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task ResolveConflicts_RevalidatesChoiceAfterHeadRace()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var laterRemote = Rename(item, "Цитрамон", T2.AddMinutes(1));
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        var conflict = Assert.Single((await harness.Sync()).Conflicts);
        harness.GitHub.CommitErrors.Enqueue(new GitHubHeadChangedException());
        harness.GitHub.RemoteAfterFailure.Enqueue(Remote("sha-r2", Snap(File(Manifest()), File(laterRemote))));

        var result = await harness.Resolve([new SyncConflictResolution(conflict, SyncConflictSide.Local)]);

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.Equal("\"Цитрамон\"", Assert.Single(result.Conflicts).RemoteValueJson);
        Assert.Equal(1, harness.GitHub.CommitCalls);
        Assert.Equal(["sha-0"], harness.GitHub.CommitParents);
        await AssertStateAsync(harness, "sha-0", @base);
    }

    [Fact]
    public async Task ResolveConflicts_CasMissKeepsLocalEdit()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var editedLocalItem = localItem.Update(
            T1.AddMinutes(5),
            localItem.Name,
            localItem.Aliases,
            localItem.Category,
            localItem.ActiveIngredients,
            localItem.Form,
            localItem.Strength,
            "во время resolve",
            localItem.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        var editedLocal = Snap(File(Manifest()), File(editedLocalItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", remote));
        var conflict = Assert.Single((await harness.Sync()).Conflicts);
        harness.Store.BeforeTryReplace = async (call, _, _) =>
        {
            if (call == 1)
            {
                await harness.Repository.ReplaceAsync(editedLocal);
            }
        };
        var resolution = new SyncConflictResolution(conflict, SyncConflictSide.Local);
        var expected = Merge(@base, editedLocal, remote, resolution);

        var result = await harness.Resolve([resolution]);

        Assert.Equal(SyncOutcome.Pushed, result.Outcome);
        Assert.Equal(2, harness.Store.TryReplaceCalls);
        Assert.Equal("Нурофен", ReadItem(expected, ItemId).Name);
        Assert.Equal("во время resolve", ReadItem(expected, ItemId).Description);
        Assert.True(harness.GitHub.Committed[0].HasSameFiles(expected));
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task ResolveConflicts_IsSerializedWithSyncOnTheSameGate()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        await using var harness = await Harness.CreateAsync(@base, local, Remote("sha-0", @base));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.GitHub.BlockGetSnapshot = hold.Task;

        var firstTask = harness.Sync();
        await harness.GitHub.EnteredGetSnapshot.Task;
        var secondTask = harness.Resolve([]);
        await Task.Delay(150);

        Assert.Equal(1, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(1, harness.GitHub.MaxGitHubInFlight);

        hold.SetResult();
        var first = await firstTask;
        var second = await secondTask;
        var expected = Merge(@base, local, @base);

        Assert.Equal(SyncOutcome.Pushed, first.Outcome);
        Assert.Equal(SyncOutcome.UpToDate, second.Outcome);
        Assert.Equal(1, harness.GitHub.MaxGitHubInFlight);
        await AssertStateAsync(harness, "commit-1", expected);
    }

    [Fact]
    public async Task ResolveConflicts_HonorsCancellationToken()
    {
        var snapshot = Snap(File(Manifest()), File(Item(ItemId, "Ибупрофен")));
        await using var harness = await Harness.CreateAsync(snapshot, snapshot, Remote("sha-0", snapshot));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Resolve([], cts.Token));

        Assert.Equal(0, harness.GitHub.GetSnapshotCalls);
        Assert.Equal(0, harness.GitHub.CommitCalls);
        await AssertStateAsync(harness, "sha-0", snapshot);
    }

    private static async Task AssertStateAsync(Harness harness, string commitSha, DataSnapshot snapshot)
    {
        var state = await harness.StateStore.LoadAsync();
        Assert.NotNull(state);
        Assert.Equal(DatasetId, state.DatasetId);
        Assert.Equal(commitSha, state.LastCommitSha);
        Assert.True(state.GetBaseSnapshot().HasSameFiles(snapshot));
    }

    private static DataSnapshot Merge(
        DataSnapshot @base,
        DataSnapshot local,
        DataSnapshot remote,
        params SyncConflictResolution[] resolutions)
    {
        var result = new SnapshotMergeEngine().Merge(@base, local, remote, MergedAt, resolutions);
        Assert.False(result.HasConflicts);
        Assert.NotNull(result.MergedSnapshot);
        return result.MergedSnapshot;
    }

    private static InventoryItem ReadItem(DataSnapshot snapshot, string id) =>
        JsonSerializer.Deserialize<InventoryItem>(snapshot.Files[$"items/{id}.json"], AptechkaJson.Options)!;

    private static Package ReadPackage(DataSnapshot snapshot, string id) =>
        JsonSerializer.Deserialize<Package>(snapshot.Files[$"packages/{id}.json"], AptechkaJson.Options)!;

    private static ShoppingItem ReadShopping(DataSnapshot snapshot, string id) =>
        JsonSerializer.Deserialize<ShoppingItem>(snapshot.Files[$"shopping/{id}.json"], AptechkaJson.Options)!;

    private static InventoryItem Item(
        string id,
        string name,
        InventoryItemCategory category = InventoryItemCategory.Medicine) =>
        InventoryItem.Create(
            id,
            T0,
            name,
            [],
            category,
            category == InventoryItemCategory.Medicine ? ["ибупрофен"] : [],
            category == InventoryItemCategory.Medicine ? "таблетки" : null,
            category == InventoryItemCategory.Medicine ? "200 мг" : null,
            null,
            category == InventoryItemCategory.Medicine);

    private static InventoryItem Rename(InventoryItem item, string name, DateTimeOffset at) =>
        item.Update(
            at,
            name,
            item.Aliases,
            item.Category,
            item.ActiveIngredients,
            item.Form,
            item.Strength,
            item.Description,
            item.KeepInStock);

    private static InventoryItem Describe(InventoryItem item, string description, DateTimeOffset at) =>
        item.Update(
            at,
            item.Name,
            item.Aliases,
            item.Category,
            item.ActiveIngredients,
            item.Form,
            item.Strength,
            description,
            item.KeepInStock);

    private static Package Package(string id, string itemId) =>
        Domain.Inventory.Package.Create(
            id,
            T0,
            itemId,
            null,
            new DateOnly(2027, 4, 30),
            ExpirationPrecision.Month,
            null,
            null,
            StockState.Available,
            null);

    private static DatasetManifest Manifest() =>
        new(DatasetManifest.CurrentSchemaVersion, DatasetId, T0);

    private static GitHubRemoteSnapshot Remote(string sha, DataSnapshot data) =>
        new(sha, "tree-" + sha, data);

    private static DataSnapshot Snap(params (string Path, byte[] Content)[] files) =>
        new(files.ToDictionary(static file => file.Path, static file => file.Content, StringComparer.Ordinal));

    private static (string Path, byte[] Content) File(InventoryItem item) => ($"items/{item.Id}.json", Bytes(item));

    private static (string Path, byte[] Content) File(Package package) => ($"packages/{package.Id}.json", Bytes(package));

    private static (string Path, byte[] Content) File(ShoppingItem shopping) => ($"shopping/{shopping.ItemId}.json", Bytes(shopping));

    private static (string Path, byte[] Content) File(DatasetManifest manifest) => ("aptechka.json", Bytes(manifest));

    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, AptechkaJson.Options);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string rootPath;

        private Harness(
            string rootPath,
            FileInventoryRepository repository,
            InterceptingSnapshotStore store,
            SyncStateStore stateStore,
            FakeGitHubDataClient gitHub,
            GitHubSyncService service)
        {
            this.rootPath = rootPath;
            Repository = repository;
            Store = store;
            StateStore = stateStore;
            GitHub = gitHub;
            Service = service;
        }

        public FileInventoryRepository Repository { get; }

        public InterceptingSnapshotStore Store { get; }

        public SyncStateStore StateStore { get; }

        public FakeGitHubDataClient GitHub { get; }

        public GitHubSyncService Service { get; }

        public static async Task<Harness> CreateAsync(
            DataSnapshot? baseSnapshot,
            DataSnapshot local,
            GitHubRemoteSnapshot remote)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "aptechka-sync-tests", Guid.NewGuid().ToString("N"));
            var clock = new StubClock(MergedAt);
            var repository = new FileInventoryRepository(
                Path.Combine(rootPath, "data"),
                clock,
                new SequenceIdGenerator());
            if (!local.IsEmpty)
            {
                await repository.ReplaceAsync(local);
            }

            var store = new InterceptingSnapshotStore(repository);
            var stateStore = new SyncStateStore(Path.Combine(rootPath, "sync", "state.json"), clock);
            if (baseSnapshot is not null)
            {
                await stateStore.SaveAsync(DatasetId, remote.CommitSha, baseSnapshot, SyncOutcome.Pushed);
            }

            var gitHub = new FakeGitHubDataClient { Remote = remote };
            var service = new GitHubSyncService(store, stateStore, gitHub, clock);
            return new Harness(rootPath, repository, store, stateStore, gitHub, service);
        }

        public Task<SyncResult> Sync(CancellationToken cancellationToken = default) =>
            Service.SyncAsync(new SyncTarget("owner", "repo", "sync-branch"), Token, "test-device", cancellationToken);

        public Task<SyncResult> Resolve(
            IReadOnlyList<SyncConflictResolution> resolutions,
            CancellationToken cancellationToken = default) =>
            Service.ResolveConflictsAsync(
                new SyncTarget("owner", "repo", "sync-branch"),
                Token,
                "test-device",
                resolutions,
                cancellationToken);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class InterceptingSnapshotStore(IDataSnapshotStore inner) : IDataSnapshotStore
    {
        public int TryReplaceCalls { get; private set; }
        public IOException? ReadError { get; set; }
        public bool AlwaysMissReplace { get; set; }

        public Func<int, DataSnapshot, DataSnapshot, Task>? BeforeTryReplace { get; set; }

        public Task<DataSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            ReadError is not null ? throw ReadError : inner.ReadAsync(cancellationToken);

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) =>
            inner.EnsureInitializedAsync(cancellationToken);

        public Task ReplaceAsync(DataSnapshot snapshot, CancellationToken cancellationToken = default) =>
            inner.ReplaceAsync(snapshot, cancellationToken);

        public async Task<bool> TryReplaceAsync(
            DataSnapshot expected,
            DataSnapshot replacement,
            CancellationToken cancellationToken = default)
        {
            var call = ++TryReplaceCalls;
            if (BeforeTryReplace is not null)
            {
                await BeforeTryReplace(call, expected, replacement);
            }

            return AlwaysMissReplace ? false : await inner.TryReplaceAsync(expected, replacement, cancellationToken);
        }
    }

    private sealed class FakeGitHubDataClient : IGitHubDataClient
    {
        public GitHubRemoteSnapshot? Remote { get; set; }

        public int GetSnapshotCalls { get; private set; }

        public int CommitCalls { get; private set; }

        public int MaxGitHubInFlight { get; private set; }

        public Task? BlockGetSnapshot { get; set; }

        public TaskCompletionSource EnteredGetSnapshot { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> CommitParents { get; } = [];

        public List<DataSnapshot> Committed { get; } = [];

        public Queue<Exception> CommitErrors { get; } = [];

        public Queue<GitHubRemoteSnapshot> RemoteAfterFailure { get; } = [];

        public Func<GitHubRemoteSnapshot, DataSnapshot, CancellationToken, Task>? BeforeCommit { get; set; }

        private int gitHubInFlight;

        public async Task<GitHubRemoteSnapshot?> GetSnapshotAsync(
            SyncTarget target,
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnterGitHub();
            try
            {
                GetSnapshotCalls++;
                EnteredGetSnapshot.TrySetResult();
                if (BlockGetSnapshot is not null && GetSnapshotCalls == 1)
                {
                    await BlockGetSnapshot;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return Remote;
            }
            finally
            {
                LeaveGitHub();
            }
        }

        public Task InitializeRepositoryAsync(
            SyncTarget target,
            string accessToken,
            byte[] manifestContent,
            string deviceName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<string> CommitSnapshotAsync(
            SyncTarget target,
            string accessToken,
            GitHubRemoteSnapshot remote,
            DataSnapshot local,
            string deviceName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnterGitHub();
            try
            {
                CommitCalls++;
                CommitParents.Add(remote.CommitSha);
                Committed.Add(local);
                if (BeforeCommit is not null)
                {
                    await BeforeCommit(remote, local, cancellationToken);
                }

                if (CommitErrors.Count > 0)
                {
                    var error = CommitErrors.Dequeue();
                    if (RemoteAfterFailure.Count > 0)
                    {
                        Remote = RemoteAfterFailure.Dequeue();
                    }

                    throw error;
                }

                var sha = $"commit-{CommitCalls}";
                Remote = new GitHubRemoteSnapshot(sha, "tree-" + sha, local);
                return sha;
            }
            finally
            {
                LeaveGitHub();
            }
        }

        private void EnterGitHub()
        {
            var inFlight = Interlocked.Increment(ref gitHubInFlight);
            if (inFlight > MaxGitHubInFlight)
            {
                MaxGitHubInFlight = inFlight;
            }
        }

        private void LeaveGitHub() => Interlocked.Decrement(ref gitHubInFlight);
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
}
