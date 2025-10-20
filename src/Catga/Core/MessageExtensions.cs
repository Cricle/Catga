using System.Runtime.CompilerServices;
using Catga.DistributedId;

namespace Catga.Core;

/// <summary>
/// Extension methods for message creation with proper ID generation.
/// </summary>
public static class MessageExtensions
{
    // Lazy-initialized SnowflakeIdGenerator (workerId from environment or random)
    private static readonly Lazy<IDistributedIdGenerator> MessageIdGenerator = new(() =>
    {
        var workerId = GetWorkerId("CATGA_WORKER_ID");
        return new SnowflakeIdGenerator(workerId);
    });

    /// <summary>
    /// Generates a new MessageId as a long using Snowflake algorithm.
    /// 92% memory reduction compared to string GUID, ordered by time, traceable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewMessageId() => MessageIdGenerator.Value.NextId();

    /// <summary>
    /// Generates a new CorrelationId as a long using Snowflake algorithm.
    /// 92% memory reduction compared to string GUID, ordered by time, traceable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewCorrelationId() => MessageIdGenerator.Value.NextId();

    /// <summary>
    /// Get worker ID from environment variable or generate a random one
    /// </summary>
    private static int GetWorkerId(string envVarName)
    {
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out var workerId))
        {
            // Validate worker ID is within valid range (0-255 for default 44-8-11 Snowflake layout)
            if (workerId >= 0 && workerId <= 255)
                return workerId;
        }

        // Generate a random worker ID (0-255 for default 8-bit worker ID)
        // In production, this should be set via environment variable
        return Random.Shared.Next(0, 256);
    }
}
