namespace Aptechka.Application.Sync;

public sealed record DataSnapshot(IReadOnlyDictionary<string, byte[]> Files)
{
    public static DataSnapshot Empty { get; } = new(
        new Dictionary<string, byte[]>(StringComparer.Ordinal));

    public bool IsEmpty => Files.Count == 0;

    public bool HasSameFiles(DataSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Files.Count != other.Files.Count)
        {
            return false;
        }

        return Files.All(pair =>
            other.Files.TryGetValue(pair.Key, out var content) &&
            pair.Value.AsSpan().SequenceEqual(content));
    }
}
