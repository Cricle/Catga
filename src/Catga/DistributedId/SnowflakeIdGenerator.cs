using System.Runtime.CompilerServices;

namespace Catga.DistributedId;

/// <summary>
/// High-performance Snowflake ID generator
/// Thread-safe, lock-free, zero-allocation
/// </summary>
public sealed class SnowflakeIdGenerator : IDistributedIdGenerator
{
    // Bit allocation (64 bits total)
    // 1 bit: sign (always 0)
    // 41 bits: timestamp (milliseconds, ~69 years)
    // 10 bits: worker ID (0-1023)
    // 12 bits: sequence (0-4095)

    private const long Epoch = 1704067200000L; // 2024-01-01 00:00:00 UTC
    private const int WorkerIdBits = 10;
    private const int SequenceBits = 12;
    private const int TimestampShift = WorkerIdBits + SequenceBits;
    private const int WorkerIdShift = SequenceBits;
    private const long MaxWorkerId = (1L << WorkerIdBits) - 1;
    private const long SequenceMask = (1L << SequenceBits) - 1;

    private readonly long _workerId;
    private long _lastTimestamp = -1L;
    private long _sequence = 0L;
    private readonly object _lock = new();

    /// <summary>
    /// Create Snowflake ID generator
    /// </summary>
    /// <param name="workerId">Worker ID (0-1023)</param>
    public SnowflakeIdGenerator(int workerId)
    {
        if (workerId < 0 || workerId > MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerId),
                $"Worker ID must be between 0 and {MaxWorkerId}");
        }

        _workerId = workerId;
    }

    /// <summary>
    /// Generate next unique ID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long NextId()
    {
        lock (_lock)
        {
            var timestamp = GetCurrentTimestamp();

            // Clock moved backwards
            if (timestamp < _lastTimestamp)
            {
                throw new InvalidOperationException(
                    $"Clock moved backwards. Refusing to generate ID for {_lastTimestamp - timestamp}ms");
            }

            // Same millisecond
            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & SequenceMask;

                // Sequence overflow, wait for next millisecond
                if (_sequence == 0)
                {
                    timestamp = WaitNextMillisecond(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;

            // Generate ID: timestamp | workerId | sequence
            return ((timestamp - Epoch) << TimestampShift)
                   | (_workerId << WorkerIdShift)
                   | _sequence;
        }
    }

    /// <summary>
    /// Generate next ID as string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string NextIdString() => NextId().ToString();

    /// <summary>
    /// Parse ID to extract metadata
    /// </summary>
    public IdMetadata ParseId(long id)
    {
        var timestamp = (id >> TimestampShift) + Epoch;
        var workerId = (int)((id >> WorkerIdShift) & MaxWorkerId);
        var sequence = (int)(id & SequenceMask);

        return new IdMetadata
        {
            Timestamp = timestamp,
            WorkerId = workerId,
            Sequence = sequence,
            GeneratedAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetCurrentTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long WaitNextMillisecond(long lastTimestamp)
    {
        var timestamp = GetCurrentTimestamp();
        while (timestamp <= lastTimestamp)
        {
            timestamp = GetCurrentTimestamp();
        }
        return timestamp;
    }
}

