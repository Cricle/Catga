using System.Runtime.CompilerServices;
using System.Threading;

namespace Catga.DistributedId;

/// <summary>
/// High-performance Snowflake ID generator
/// Thread-safe, zero-allocation, 100% lock-free, configurable bit layout
/// Uses pure CAS (Compare-And-Swap) loop for true lock-free concurrency
/// </summary>
public sealed class SnowflakeIdGenerator : IDistributedIdGenerator
{
    private readonly long _workerId;
    private readonly SnowflakeBitLayout _layout;
    
    // Packed state: timestamp (high 52 bits) | sequence (low 12 bits)
    // This allows atomic updates using a single Interlocked.CompareExchange
    // Initialize to 0 (timestamp=0, sequence=0)
    private long _packedState = 0L;

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
    /// Generate next unique ID (zero-allocation, 100% lock-free)
    /// Uses CAS (Compare-And-Swap) loop - no locks, no SpinLock
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long NextId()
    {
        SpinWait spinWait = default;

        while (true)
        {
            // Read current packed state atomically
            var currentState = Interlocked.Read(ref _packedState);
            var lastTimestamp = UnpackTimestamp(currentState);
            var lastSequence = UnpackSequence(currentState);

            // Get current timestamp
            var timestamp = GetCurrentTimestamp();

            // Clock moved backwards - fail fast
            if (timestamp < lastTimestamp)
            {
                throw new InvalidOperationException(
                    $"Clock moved backwards. Refusing to generate ID for {lastTimestamp - timestamp}ms");
            }

            long newSequence;
            long newTimestamp;

            if (timestamp == lastTimestamp)
            {
                // Same millisecond: increment sequence
                newSequence = (lastSequence + 1) & _layout.SequenceMask;

                if (newSequence == 0)
                {
                    // Sequence overflow: wait for next millisecond
                    timestamp = WaitNextMillisecond(lastTimestamp);
                    newTimestamp = timestamp;
                    newSequence = 0;
                }
                else
                {
                    newTimestamp = timestamp;
                }
            }
            else
            {
                // New millisecond: reset sequence
                newTimestamp = timestamp;
                newSequence = 0;
            }

            // Pack new state
            var newState = PackState(newTimestamp, newSequence);

            // Try to update state atomically (CAS)
            if (Interlocked.CompareExchange(ref _packedState, newState, currentState) == currentState)
            {
                // Success! Generate and return ID
                return ((newTimestamp - _layout.EpochMilliseconds) << _layout.TimestampShift)
                       | (_workerId << _layout.WorkerIdShift)
                       | newSequence;
            }

            // CAS failed: another thread updated the state, retry
            spinWait.SpinOnce();
        }
    }

    /// <summary>
    /// Pack timestamp and sequence into a single long
    /// Uses layout-specific sequence bits
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long PackState(long timestamp, long sequence)
    {
        return (timestamp << _layout.SequenceBits) | sequence;
    }

    /// <summary>
    /// Unpack timestamp from packed state
    /// Uses layout-specific sequence bits
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long UnpackTimestamp(long state)
    {
        return state >> _layout.SequenceBits;
    }

    /// <summary>
    /// Unpack sequence from packed state
    /// Uses layout-specific sequence mask
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long UnpackSequence(long state)
    {
        return state & _layout.SequenceMask;
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
    /// Batch generate IDs into a span (0 allocation, lock-free)
    /// Uses optimized batch reservation to reduce CAS contention
    /// </summary>
    public int NextIds(Span<long> destination)
    {
        if (destination.Length == 0)
        {
            return 0;
        }

        var count = destination.Length;
        var generated = 0;
        SpinWait spinWait = default;

        while (generated < count)
        {
            // Read current packed state atomically
            var currentState = Interlocked.Read(ref _packedState);
            var lastTimestamp = UnpackTimestamp(currentState);
            var lastSequence = UnpackSequence(currentState);

            // Get current timestamp
            var timestamp = GetCurrentTimestamp();

            // Clock moved backwards - fail fast
            if (timestamp < lastTimestamp)
            {
                throw new InvalidOperationException(
                    $"Clock moved backwards. Refusing to generate IDs for {lastTimestamp - timestamp}ms");
            }

            long startSequence;
            long batchSize;
            long newTimestamp;

            if (timestamp == lastTimestamp)
            {
                // Same millisecond: try to reserve a batch of sequences
                var available = _layout.SequenceMask - lastSequence;
                batchSize = Math.Min(count - generated, (int)available);

                if (batchSize == 0)
                {
                    // Sequence exhausted: wait for next millisecond
                    timestamp = WaitNextMillisecond(lastTimestamp);
                    newTimestamp = timestamp;
                    startSequence = 0;
                    batchSize = Math.Min(count - generated, (int)_layout.SequenceMask + 1);
                }
                else
                {
                    newTimestamp = timestamp;
                    startSequence = lastSequence + 1;
                }
            }
            else
            {
                // New millisecond: can reserve from sequence 0
                newTimestamp = timestamp;
                startSequence = 0;
                batchSize = Math.Min(count - generated, (int)_layout.SequenceMask + 1);
            }

            var endSequence = startSequence + batchSize - 1;
            var newState = PackState(newTimestamp, endSequence);

            // Try to reserve the batch atomically (CAS)
            if (Interlocked.CompareExchange(ref _packedState, newState, currentState) == currentState)
            {
                // Success! Generate IDs for the reserved batch
                var epochOffset = newTimestamp - _layout.EpochMilliseconds;
                for (int i = 0; i < batchSize; i++)
                {
                    var seq = startSequence + i;
                    destination[generated++] = (epochOffset << _layout.TimestampShift)
                                               | (_workerId << _layout.WorkerIdShift)
                                               | seq;
                }
            }
            else
            {
                // CAS failed: another thread updated the state, retry
                spinWait.SpinOnce();
            }
        }

        return generated;
    }

    /// <summary>
    /// Batch generate IDs into an array (allocates array)
    /// </summary>
    public long[] NextIds(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0");
        }

        var ids = new long[count];
        NextIds(ids.AsSpan());
        return ids;
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
        var timestamp = (id >> _layout.TimestampShift) + _layout.EpochMilliseconds;
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

