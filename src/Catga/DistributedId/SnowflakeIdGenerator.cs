using System.Runtime.CompilerServices;

namespace Catga.DistributedId;

/// <summary>
/// High-performance Snowflake ID generator
/// Thread-safe, zero-allocation, configurable bit layout
/// </summary>
public sealed class SnowflakeIdGenerator : IDistributedIdGenerator
{
    private const long Epoch = 1704067200000L; // 2024-01-01 00:00:00 UTC

    private readonly long _workerId;
    private readonly SnowflakeBitLayout _layout;
    private long _lastTimestamp = -1L;
    private long _sequence = 0L;
    private readonly object _lock = new();

    // Cached string builder for zero-allocation string generation
    private readonly System.Buffers.ArrayPool<char> _charPool = System.Buffers.ArrayPool<char>.Shared;

    /// <summary>
    /// Create Snowflake ID generator with default layout
    /// </summary>
    /// <param name="workerId">Worker ID</param>
    public SnowflakeIdGenerator(int workerId)
        : this(workerId, SnowflakeBitLayout.Default)
    {
    }

    /// <summary>
    /// Create Snowflake ID generator with custom bit layout
    /// </summary>
    /// <param name="workerId">Worker ID</param>
    /// <param name="layout">Bit layout configuration</param>
    public SnowflakeIdGenerator(int workerId, SnowflakeBitLayout layout)
    {
        layout.Validate();

        if (workerId < 0 || workerId > layout.MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerId),
                $"Worker ID must be between 0 and {layout.MaxWorkerId} for layout {layout}");
        }

        _workerId = workerId;
        _layout = layout;
    }

    /// <summary>
    /// Generate next unique ID (zero-allocation)
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
                _sequence = (_sequence + 1) & _layout.SequenceMask;

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
            return ((timestamp - Epoch) << _layout.TimestampShift)
                   | (_workerId << _layout.WorkerIdShift)
                   | _sequence;
        }
    }

    /// <summary>
    /// Generate next ID as string (zero-allocation path available)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string NextIdString()
    {
        var id = NextId();
        return id.ToString();
    }

    /// <summary>
    /// Try write next ID to span (zero-allocation)
    /// </summary>
    public bool TryWriteNextId(Span<char> destination, out int charsWritten)
    {
        var id = NextId();
        return id.TryFormat(destination, out charsWritten);
    }

    /// <summary>
    /// Parse ID to extract metadata (zero-allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ParseId(long id, out IdMetadata metadata)
    {
        var timestamp = (id >> _layout.TimestampShift) + Epoch;
        var workerId = (int)((id >> _layout.WorkerIdShift) & _layout.MaxWorkerId);
        var sequence = (int)(id & _layout.SequenceMask);

        metadata = new IdMetadata
        {
            Timestamp = timestamp,
            WorkerId = workerId,
            Sequence = sequence,
            GeneratedAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
        };
    }

    /// <summary>
    /// Parse ID to extract metadata (allocating version for compatibility)
    /// </summary>
    public IdMetadata ParseId(long id)
    {
        ParseId(id, out var metadata);
        return metadata;
    }

    /// <summary>
    /// Get current bit layout
    /// </summary>
    public SnowflakeBitLayout GetLayout() => _layout;

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

