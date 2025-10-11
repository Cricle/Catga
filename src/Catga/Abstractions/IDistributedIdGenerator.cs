namespace Catga.DistributedId;

/// <summary>
/// Distributed ID generator interface
/// </summary>
public interface IDistributedIdGenerator
{
    /// <summary>
    /// Generate next ID as long (0 allocation)
    /// </summary>
    public long NextId();

    /// <summary>
    /// Generate next ID as string
    /// </summary>
    public string NextIdString();

    /// <summary>
    /// Try to write next ID to a span (0 allocation)
    /// </summary>
    public bool TryWriteNextId(Span<char> destination, out int charsWritten);

    /// <summary>
    /// Try to generate next ID without throwing exceptions (P2 optimization)
    /// </summary>
    /// <param name="id">Generated ID if successful</param>
    /// <returns>True if successful, false if clock moved backwards</returns>
    public bool TryNextId(out long id);

    /// <summary>
    /// Batch generate IDs into a span (0 allocation, lock-free)
    /// </summary>
    /// <param name="destination">Span to fill with generated IDs</param>
    /// <returns>Number of IDs successfully generated</returns>
    public int NextIds(Span<long> destination);

    /// <summary>
    /// Batch generate IDs into an array (allocates array)
    /// </summary>
    /// <param name="count">Number of IDs to generate</param>
    /// <returns>Array of generated IDs</returns>
    public long[] NextIds(int count);

    /// <summary>
    /// Parse ID to get metadata (allocates struct)
    /// </summary>
    public IdMetadata ParseId(long id);

    /// <summary>
    /// Parse ID to get metadata (0 allocation version)
    /// </summary>
    public void ParseId(long id, out IdMetadata metadata);

    /// <summary>
    /// Get current bit layout configuration
    /// </summary>
    public SnowflakeBitLayout GetLayout();
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

