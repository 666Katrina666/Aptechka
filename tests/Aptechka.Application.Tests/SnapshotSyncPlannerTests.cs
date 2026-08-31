using System.Text;
using Aptechka.Application.Sync;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

public sealed class SnapshotSyncPlannerTests
{
    private static readonly DataSnapshot ManifestOnly = Snapshot(
        ("aptechka.json", "manifest"));

    private static readonly DataSnapshot WithItem = Snapshot(
        ("aptechka.json", "manifest"),
        ("items/item.json", "item"));

    [Fact]
    public void Plan_WithoutBase_PushesCompatibleLocalSuperset()
    {
        var action = SnapshotSyncPlanner.Plan(null, WithItem, ManifestOnly);

        Assert.Equal(SnapshotSyncAction.Push, action);
    }

    [Fact]
    public void Plan_WithoutBase_PullsCompatibleRemoteSuperset()
    {
        var action = SnapshotSyncPlanner.Plan(null, ManifestOnly, WithItem);

        Assert.Equal(SnapshotSyncAction.Pull, action);
    }

    [Fact]
    public void Plan_WithBase_PushesOneSidedLocalChange()
    {
        var action = SnapshotSyncPlanner.Plan(ManifestOnly, WithItem, ManifestOnly);

        Assert.Equal(SnapshotSyncAction.Push, action);
    }

    [Fact]
    public void Plan_WithBase_StopsConcurrentChanges()
    {
        var local = Snapshot(("aptechka.json", "manifest"), ("items/item.json", "local"));
        var remote = Snapshot(("aptechka.json", "manifest"), ("items/item.json", "remote"));

        var action = SnapshotSyncPlanner.Plan(ManifestOnly, local, remote);

        Assert.Equal(SnapshotSyncAction.Conflict, action);
    }

    private static DataSnapshot Snapshot(params (string Path, string Content)[] files) => new(
        files.ToDictionary(
            static file => file.Path,
            static file => Encoding.UTF8.GetBytes(file.Content),
            StringComparer.Ordinal));
}
