using Aptechka.Domain.Identity;

namespace Aptechka.Domain.Tests;

public sealed class UlidGeneratorTests
{
    [Fact]
    public void Create_ReturnsValidDistinctIdentifiers()
    {
        var timestamp = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

        var first = UlidGenerator.Create(timestamp);
        var second = UlidGenerator.Create(timestamp);

        Assert.True(UlidGenerator.IsValid(first));
        Assert.True(UlidGenerator.IsValid(second));
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAI")]
    [InlineData("81ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public void IsValid_RejectsInvalidValue(string? value)
    {
        Assert.False(UlidGenerator.IsValid(value));
    }
}
