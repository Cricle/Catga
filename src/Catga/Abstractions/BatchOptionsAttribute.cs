using System;

namespace Catga.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BatchOptionsAttribute : Attribute
{
    public int MaxBatchSize { get; init; } = 0; // 0 => not set; generator fills defaults
    public int BatchTimeoutMs { get; init; } = 0; // 0 => not set
    public int MaxQueueLength { get; init; } = 0; // 0 => not set
    public int ShardIdleTtlMs { get; init; } = 0; // 0 => not set
    public int MaxShards { get; init; } = 0; // 0 => not set

    // Flush execution mode: 0 = Sequential (default), >0 means LimitedConcurrency with that degree
    public int FlushDegree { get; init; } = 0;
}
