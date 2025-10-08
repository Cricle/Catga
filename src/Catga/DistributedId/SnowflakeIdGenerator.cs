using System.Runtime.CompilerServices;
using System.Threading;

namespace Catga.DistributedId;

/// <summary>
/// High-performance Snowflake ID generator
/// Thread-safe, zero-allocation, 100% lock-free, configurable bit layout
/// Uses pure CAS (Compare-And-Swap) loop for true lock-free concurrency
/// P3: Cache line padding to avoid false sharing in high-contention scenarios
/// </summary>
public sealed class SnowflakeIdGenerator : IDistributedIdGenerator
{
    private readonly long _workerId;
    private readonly SnowflakeBitLayout _layout;

    // P3: Cache line padding (64 bytes before hot field)
    #pragma warning disable CS0169 // Field is never used (padding for false sharing prevention)
    private long _padding1;
    private long _padding2;
    private long _padding3;
    private long _padding4;
    private long _padding5;
    private long _padding6;
    private long _padding7;
    #pragma warning restore CS0169

    // Packed state: timestamp (high 52 bits) | sequence (low 12 bits)
    // This allows atomic updates using a single Interlocked.CompareExchange
    // Initialize to 0 (timestamp=0, sequence=0)
    // P3: Aligned on its own cache line to prevent false sharing
    private long _packedState = 0L;

    // P3: Cache line padding (64 bytes after hot field)
    #pragma warning disable CS0169 // Field is never used (padding for false sharing prevention)
    private long _padding8;
    private long _padding9;
    private long _padding10;
    private long _padding11;
    private long _padding12;
    private long _padding13;
    private long _padding14;
    #pragma warning restore CS0169

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
    /// Try to generate next ID without throwing exceptions (P2 optimization)
    /// Returns false instead of throwing when clock moves backwards
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryNextId(out long id)
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

            // P2: Return false instead of throwing
            if (timestamp < lastTimestamp)
            {
                id = 0;
                return false; // Clock moved backwards
            }

            long newSequence;
            long newTimestamp;

            if (timestamp == lastTimestamp)
            {
                // Same millisecond: increment sequence
                newSequence = lastSequence + 1;

                if (newSequence > _layout.SequenceMask)
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

            // Try to update state using CAS
            var newState = PackState(newTimestamp, newSequence);
            if (Interlocked.CompareExchange(ref _packedState, newState, currentState) == currentState)
            {
                // Success! Generate ID
                id = ((newTimestamp - _layout.EpochMilliseconds) << _layout.TimestampShift)
                     | (_workerId << _layout.WorkerIdShift)
                     | newSequence;
                return true;
            }

            // CAS failed: another thread updated the state, retry
            spinWait.SpinOnce();
        }
    }

    /// <summary>
    /// Batch generate IDs into a span (0 allocation, lock-free)
    /// Uses optimized batch reservation to reduce CAS contention
    /// P1 Optimization: Adaptive batch sizing for ultra-large requests (>10k IDs)
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

        // P1: For ultra-large batches (>10k), use adaptive reservation strategy
        var maxBatchPerIteration = count > 10000
            ? Math.Min((int)_layout.SequenceMask + 1, count / 4) // Reserve up to 25% at a time
            : (int)_layout.SequenceMask + 1; // Normal batching

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
                batchSize = Math.Min(Math.Min(count - generated, maxBatchPerIteration), (int)available);

                if (batchSize == 0)
                {
                    // Sequence exhausted: wait for next millisecond
                    timestamp = WaitNextMillisecond(lastTimestamp);
                    newTimestamp = timestamp;
                    startSequence = 0;
                    batchSize = Math.Min(count - generated, maxBatchPerIteration);
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
                batchSize = Math.Min(count - generated, maxBatchPerIteration);
            }

            var endSequence = startSequence + batchSize - 1;
            var newState = PackState(newTimestamp, endSequence);

            // Try to reserve the batch atomically (CAS)
            if (Interlocked.CompareExchange(ref _packedState, newState, currentState) == currentState)
            {
                // Success! Generate IDs for the reserved batch
                // P1: Precompute common values outside loop
                var epochOffset = newTimestamp - _layout.EpochMilliseconds;
                var baseId = (epochOffset << _layout.TimestampShift) | (_workerId << _layout.WorkerIdShift);

                for (int i = 0; i < batchSize; i++)
                {
                    var seq = startSequence + i;
                    destination[generated++] = baseId | seq;
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

