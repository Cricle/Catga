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
    /// Custom epoch (start time) in milliseconds
    /// Default: 2024-01-01 00:00:00 UTC
    /// </summary>
    public long EpochMilliseconds { get; init; }

    /// <summary>
    /// Default constructor (required for struct with init properties)
    /// </summary>
    public SnowflakeBitLayout()
    {
        TimestampBits = 0;
        WorkerIdBits = 0;
        SequenceBits = 0;
        EpochMilliseconds = 1704067200000L;
    }

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
    /// Default layout (44-8-11)
    /// 44 bits timestamp (~557 years from epoch)
    /// 8 bits worker ID (256 workers)
    /// 11 bits sequence (2048 IDs/ms)
    /// Epoch: 2024-01-01 00:00:00 UTC
    ///
    /// This layout ensures the framework can be used for 500+ years
    /// while supporting 256 workers and 2048 IDs per millisecond (2M IDs/sec per worker)
    /// </summary>
    public static SnowflakeBitLayout Default => new()
    {
        TimestampBits = 44,
        WorkerIdBits = 8,
        SequenceBits = 11,
        EpochMilliseconds = 1704067200000L  // 2024-01-01 00:00:00 UTC
    };

    /// <summary>
    /// Create layout with custom epoch (uses default 500+ year layout)
    /// </summary>
    public static SnowflakeBitLayout WithEpoch(DateTime epoch)
    {
        return new SnowflakeBitLayout
        {
            TimestampBits = 44,
            WorkerIdBits = 8,
            SequenceBits = 11,
            EpochMilliseconds = new DateTimeOffset(epoch.ToUniversalTime()).ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    /// Create layout with custom epoch and bit allocation
    /// </summary>
    public static SnowflakeBitLayout Create(
        DateTime epoch,
        int timestampBits = 44,
        int workerIdBits = 8,
        int sequenceBits = 11)
    {
        return new SnowflakeBitLayout
        {
            TimestampBits = timestampBits,
            WorkerIdBits = workerIdBits,
            SequenceBits = sequenceBits,
            EpochMilliseconds = new DateTimeOffset(epoch.ToUniversalTime()).ToUnixTimeMilliseconds()
        };
    }

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
    /// Ultra long lifespan layout (46-6-11)
    /// 46 bits timestamp (~2,234 years from epoch)
    /// 6 bits worker ID (64 workers)
    /// 11 bits sequence (2048 IDs/ms)
    ///
    /// For applications that need to last millennia
    /// </summary>
    public static SnowflakeBitLayout UltraLongLifespan => new()
    {
        TimestampBits = 46,
        WorkerIdBits = 6,
        SequenceBits = 11,
        EpochMilliseconds = 1704067200000L
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
    /// Get epoch as DateTime
    /// </summary>
    public DateTime GetEpoch() =>
        DateTimeOffset.FromUnixTimeMilliseconds(EpochMilliseconds).UtcDateTime;

    /// <summary>
    /// Get layout description
    /// </summary>
    public override string ToString() =>
        $"Snowflake Layout: {TimestampBits}-{WorkerIdBits}-{SequenceBits} " +
        $"(~{MaxYears}y, {MaxWorkerId + 1} workers, {MaxSequence + 1} IDs/ms, " +
        $"Epoch: {GetEpoch():yyyy-MM-dd})";
}

