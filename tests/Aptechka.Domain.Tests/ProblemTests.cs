using Aptechka.Domain.Inventory;

namespace Aptechka.Domain.Tests;

public sealed class ProblemTests
{
    private const string ProblemId = "01ARZ3NDEKTSV4RRFFQ69G5FB0";
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Create_BuildsValidProblem()
    {
        var problem = Problem.Create(
            ProblemId,
            Now,
            "  Головная боль  ",
            [" болит голова "],
            [ItemId],
            null);

        Assert.Equal(ProblemId, problem.Id);
        Assert.Equal(1, problem.Revision);
        Assert.Equal(Now, problem.CreatedAt);
        Assert.Equal(Now, problem.UpdatedAt);
        Assert.Null(problem.DeletedAt);
        Assert.Equal("Головная боль", problem.Name);
        Assert.Equal(["болит голова"], problem.Aliases);
        Assert.Equal([ItemId], problem.ItemIds);
        Assert.Null(problem.Note);
    }

    [Fact]
    public void Create_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => Problem.Create(
            ProblemId,
            Now,
            "   ",
            [],
            [],
            null));
    }

    [Fact]
    public void Create_NormalizesAndDeduplicatesAliasesIgnoringCase()
    {
        var problem = Problem.Create(
            ProblemId,
            Now,
            "Головная боль",
            ["болит голова", "  Болит Голова  ", "БОЛИТ ГОЛОВА", "", "  "],
            [],
            null);

        Assert.Equal(["болит голова"], problem.Aliases);
    }

    [Fact]
    public void Create_ValidatesAndDeduplicatesItemIds()
    {
        var problem = Problem.Create(
            ProblemId,
            Now,
            "Головная боль",
            [],
            [ItemId, $" {ItemId} ", SecondItemId, ""],
            null);

        Assert.Equal([ItemId, SecondItemId], problem.ItemIds);
        Assert.Throws<ArgumentException>(() => Problem.Create(
            ProblemId,
            Now,
            "Головная боль",
            [],
            ["not-a-ulid"],
            null));
    }

    [Fact]
    public void Create_TrimsNoteAndTurnsBlankIntoNull()
    {
        var withNote = Problem.Create(ProblemId, Now, "Головная боль", [], [], "  домашняя пометка  ");
        var blank = Problem.Create(ProblemId, Now, "Головная боль", [], [], "   ");

        Assert.Equal("домашняя пометка", withNote.Note);
        Assert.Null(blank.Note);
    }

    [Fact]
    public void Delete_SetsTombstoneAndIsIdempotent()
    {
        var original = Problem.Create(ProblemId, Now, "Головная боль", [], [ItemId], null);
        var deletedAt = Now.AddMinutes(10);
        var deleted = original.Delete(deletedAt);
        var repeated = deleted.Delete(deletedAt.AddHours(1));

        Assert.Equal(2, deleted.Revision);
        Assert.Equal(deletedAt, deleted.UpdatedAt);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.Equal([ItemId], deleted.ItemIds);
        Assert.Same(deleted, repeated);
        Assert.Equal(2, repeated.Revision);
    }

    [Fact]
    public void Delete_DoesNotCascadeOrClearItemIds()
    {
        var item = InventoryItem.Create(
            ItemId,
            Now,
            "Ибупрофен",
            [],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            false);
        var problem = Problem.Create(ProblemId, Now, "Головная боль", [], [item.Id], null);

        var archivedItem = item.Delete(Now.AddMinutes(5));

        Assert.NotNull(archivedItem.DeletedAt);
        Assert.Equal([ItemId], problem.ItemIds);
        Assert.Equal([ItemId], problem.Delete(Now.AddMinutes(6)).ItemIds);
    }

    [Fact]
    public void EnsureValid_RejectsNullAliasesAndItemIds()
    {
        AssertInvalid(Valid() with { Aliases = null! });
        AssertInvalid(Valid() with { ItemIds = null! });
    }

    [Fact]
    public void EnsureValid_RejectsNullEmptyUntrimmedAndDuplicateAliases()
    {
        AssertInvalid(Valid() with { Aliases = [null!] });
        AssertInvalid(Valid() with { Aliases = [""] });
        AssertInvalid(Valid() with { Aliases = ["   "] });
        AssertInvalid(Valid() with { Aliases = [" болит голова "] });
        AssertInvalid(Valid() with { Aliases = ["болит голова", "Болит Голова"] });
    }

    [Fact]
    public void EnsureValid_RejectsNullEmptyUntrimmedDuplicateAndInvalidItemIds()
    {
        AssertInvalid(Valid() with { ItemIds = [null!] });
        AssertInvalid(Valid() with { ItemIds = [""] });
        AssertInvalid(Valid() with { ItemIds = [$" {ItemId} "] });
        AssertInvalid(Valid() with { ItemIds = [ItemId, ItemId] });
        AssertInvalid(Valid() with { ItemIds = ["not-a-ulid"] });
    }

    [Fact]
    public void EnsureValid_RejectsNonCanonicalNameAndNote()
    {
        AssertInvalid(Valid() with { Name = " Головная боль " });
        AssertInvalid(Valid() with { Note = "" });
        AssertInvalid(Valid() with { Note = "   " });
        AssertInvalid(Valid() with { Note = " домашняя " });
    }

    private static Problem Valid() =>
        Problem.Create(ProblemId, Now, "Головная боль", ["болит голова"], [ItemId], null);

    private static void AssertInvalid(Problem problem) =>
        Assert.Throws<InvalidOperationException>(problem.EnsureValid);
}
