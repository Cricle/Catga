using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.DistributedId;

namespace Catga.Core;

/// <summary>
/// Extension methods for message creation with proper ID generation.
/// </summary>
public static class MessageExtensions
{
    // User-configurable generator (null = use lazy default)
    private static IDistributedIdGenerator? _customGenerator;

    // Lazy-initialized SnowflakeIdGenerator (workerId from environment or random)
    // This is used as fallback when DI is not available and no custom generator is set
    private static readonly Lazy<IDistributedIdGenerator> DefaultGenerator = new(() => new SnowflakeIdGenerator(GetWorkerId("CATGA_WORKER_ID")));

    /// <summary>
    /// Gets the current ID generator (custom or default)
    /// </summary>
    private static IDistributedIdGenerator CurrentGenerator => _customGenerator ?? DefaultGenerator.Value;

    /// <summary>
    /// Configure a custom ID generator for all MessageId/CorrelationId generation.
    /// </summary>
    /// <param name="generator">Custom ID generator (or null to reset to default)</param>
    /// <remarks>
    /// This is useful when you need to set a specific WorkerId without using DI.
    /// Example: MessageExtensions.SetIdGenerator(new SnowflakeIdGenerator(workerId: 1))
    /// </remarks>
    public static void SetIdGenerator(IDistributedIdGenerator? generator) => _customGenerator = generator;

    /// <summary>
    /// Configure ID generator with a specific WorkerId.
    /// </summary>
    /// <param name="workerId">Worker ID (0-255 for default Snowflake layout)</param>
    /// <remarks>
    /// Shorthand for: SetIdGenerator(new SnowflakeIdGenerator(workerId))
    /// Example: MessageExtensions.UseWorkerId(1)
    /// </remarks>
    public static void UseWorkerId(int workerId)
    {
        if (workerId < 0 || workerId > 255)
            throw new ArgumentOutOfRangeException(nameof(workerId), "WorkerId must be between 0 and 255");

        SetIdGenerator(new SnowflakeIdGenerator(workerId));
    }

    /// <summary>
    /// Generates a new MessageId as a long using Snowflake algorithm.
    /// 92% memory reduction compared to string GUID, ordered by time, traceable.
    /// </summary>
    /// <remarks>
    /// Uses custom generator (if set), otherwise uses default generator.
    /// For cluster deployments, configure WorkerId:
    /// - Option 1 (DI): services.AddCatga().UseWorkerId(nodeId)
    /// - Option 2 (Static): MessageExtensions.UseWorkerId(nodeId)
    /// - Option 3 (Env): Set CATGA_WORKER_ID environment variable
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewMessageId() => CurrentGenerator.NextId();

    /// <summary>
    /// Generates a new MessageId using a specific generator (DI-friendly)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewMessageId(IDistributedIdGenerator generator) => generator.NextId();

    /// <summary>
    /// Generates a new CorrelationId as a long using Snowflake algorithm.
    /// 92% memory reduction compared to string GUID, ordered by time, traceable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewCorrelationId() => CurrentGenerator.NextId();

    /// <summary>
    /// Generates a new CorrelationId using a specific generator (DI-friendly)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NewCorrelationId(IDistributedIdGenerator generator) => generator.NextId();

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
        // In production, this should be set via environment variable or DI configuration
        return Random.Shared.Next(0, 256);
    }
}
