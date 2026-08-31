namespace Aptechka.Application.Sync;

public sealed record DataSnapshot(IReadOnlyDictionary<string, byte[]> Files)
{
    public static DataSnapshot Empty { get; } = new(
        new Dictionary<string, byte[]>(StringComparer.Ordinal));

    public bool IsEmpty => Files.Count == 0;
}
