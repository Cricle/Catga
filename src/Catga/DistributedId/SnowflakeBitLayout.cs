namespace Catga.DistributedId;

/// <summary>
/// Snowflake ID bit layout configuration
/// Allows flexible bit allocation for different requirements
/// </summary>
public readonly struct SnowflakeBitLayout
{
    /// <summary>
    /// Bits for timestamp (determines max years)
    /// Default: 41 bits (~69 years)
    /// </summary>
    public int TimestampBits { get; init; }

    /// <summary>
    /// Bits for worker ID (determines max workers)
    /// Default: 10 bits (1024 workers)
    /// </summary>
    public int WorkerIdBits { get; init; }

    /// <summary>
    /// Bits for sequence (determines IDs per millisecond)
    /// Default: 12 bits (4096 IDs/ms)
    /// </summary>
    public int SequenceBits { get; init; }

    /// <summary>
    /// Maximum timestamp value
    /// </summary>
    public long MaxTimestamp => (1L << TimestampBits) - 1;

    /// <summary>
    /// Maximum worker ID
    /// </summary>
    public long MaxWorkerId => (1L << WorkerIdBits) - 1;

    /// <summary>
    /// Maximum sequence value
    /// </summary>
    public long MaxSequence => (1L << SequenceBits) - 1;

    /// <summary>
    /// Bit shifts for ID composition
    /// </summary>
    public int TimestampShift => WorkerIdBits + SequenceBits;
    public int WorkerIdShift => SequenceBits;
    public long SequenceMask => MaxSequence;

    /// <summary>
    /// Estimated max years (based on millisecond timestamp)
    /// </summary>
    public int MaxYears => (int)(MaxTimestamp / (365.25 * 24 * 60 * 60 * 1000));

    /// <summary>
    /// Default layout (41-10-12)
    /// 41 bits timestamp (~69 years)
    /// 10 bits worker ID (1024 workers)
    /// 12 bits sequence (4096 IDs/ms)
    /// </summary>
    public static SnowflakeBitLayout Default => new()
    {
        TimestampBits = 41,
        WorkerIdBits = 10,
        SequenceBits = 12
    };

    /// <summary>
    /// Long lifespan layout (43-8-12)
    /// 43 bits timestamp (~278 years)
    /// 8 bits worker ID (256 workers)
    /// 12 bits sequence (4096 IDs/ms)
    /// </summary>
    public static SnowflakeBitLayout LongLifespan => new()
    {
        TimestampBits = 43,
        WorkerIdBits = 8,
        SequenceBits = 12
    };

    /// <summary>
    /// High concurrency layout (39-10-14)
    /// 39 bits timestamp (~17 years)
    /// 10 bits worker ID (1024 workers)
    /// 14 bits sequence (16384 IDs/ms)
    /// </summary>
    public static SnowflakeBitLayout HighConcurrency => new()
    {
        TimestampBits = 39,
        WorkerIdBits = 10,
        SequenceBits = 14
    };

    /// <summary>
    /// Large cluster layout (38-12-13)
    /// 38 bits timestamp (~8.7 years)
    /// 12 bits worker ID (4096 workers)
    /// 13 bits sequence (8192 IDs/ms)
    /// </summary>
    public static SnowflakeBitLayout LargeCluster => new()
    {
        TimestampBits = 38,
        WorkerIdBits = 12,
        SequenceBits = 13
    };

    /// <summary>
    /// Ultra long lifespan layout (45-6-12)
    /// 45 bits timestamp (~1112 years)
    /// 6 bits worker ID (64 workers)
    /// 12 bits sequence (4096 IDs/ms)
    /// </summary>
    public static SnowflakeBitLayout UltraLongLifespan => new()
    {
        TimestampBits = 45,
        WorkerIdBits = 6,
        SequenceBits = 12
    };

    /// <summary>
    /// Validate bit layout
    /// </summary>
    public void Validate()
    {
        var totalBits = TimestampBits + WorkerIdBits + SequenceBits;
        if (totalBits != 63)
        {
            throw new ArgumentException(
                $"Total bits must be 63 (1 sign bit + 63 data bits). Current: {totalBits}");
        }

        if (TimestampBits < 30 || TimestampBits > 50)
        {
            throw new ArgumentException(
                $"TimestampBits must be between 30 and 50. Current: {TimestampBits}");
        }

        if (WorkerIdBits < 0 || WorkerIdBits > 20)
        {
            throw new ArgumentException(
                $"WorkerIdBits must be between 0 and 20. Current: {WorkerIdBits}");
        }

        if (SequenceBits < 0 || SequenceBits > 20)
        {
            throw new ArgumentException(
                $"SequenceBits must be between 0 and 20. Current: {SequenceBits}");
        }
    }

    /// <summary>
    /// Get layout description
    /// </summary>
    public override string ToString() =>
        $"Snowflake Layout: {TimestampBits}-{WorkerIdBits}-{SequenceBits} " +
        $"(~{MaxYears}y, {MaxWorkerId + 1} workers, {MaxSequence + 1} IDs/ms)";
}

