namespace Catga.DistributedId;

/// <summary>
/// Distributed ID generator interface
/// </summary>
public interface IDistributedIdGenerator
{
    /// <summary>
    /// Generate next ID as long
    /// </summary>
    long NextId();

    /// <summary>
    /// Generate next ID as string
    /// </summary>
    string NextIdString();

    /// <summary>
    /// Parse ID to get metadata
    /// </summary>
    IdMetadata ParseId(long id);
}

/// <summary>
/// ID metadata extracted from generated ID
/// </summary>
public readonly struct IdMetadata
{
    public long Timestamp { get; init; }
    public int WorkerId { get; init; }
    public int Sequence { get; init; }
    public DateTime GeneratedAt { get; init; }

    public override string ToString() =>
        $"Timestamp: {Timestamp}, WorkerId: {WorkerId}, Sequence: {Sequence}, GeneratedAt: {GeneratedAt:yyyy-MM-dd HH:mm:ss.fff}";
}

