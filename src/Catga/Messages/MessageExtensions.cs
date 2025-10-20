using System.Runtime.CompilerServices;
using Catga.DistributedId;

namespace Catga.Messages;

/// <summary>
/// Extension methods for message creation with proper ID generation.
/// </summary>
public static class MessageExtensions
{
    // Singleton SnowflakeIdGenerator for MessageId generation (workerId = 1)
    private static readonly IDistributedIdGenerator MessageIdGenerator = new SnowflakeIdGenerator(workerId: 1);

    // Singleton SnowflakeIdGenerator for CorrelationId generation (workerId = 2)
    private static readonly IDistributedIdGenerator CorrelationIdGenerator = new SnowflakeIdGenerator(workerId: 2);

    /// <summary>
    /// Generates a new MessageId as a long using Snowflake algorithm.
    /// 92% memory reduction compared to string GUID, ordered by time, traceable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewMessageId() => MessageIdGenerator.NextId();

    /// <summary>
    /// Generates a new CorrelationId as a long using Snowflake algorithm.
    /// 92% memory reduction compared to string GUID, ordered by time, traceable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewCorrelationId() => CorrelationIdGenerator.NextId();
}

