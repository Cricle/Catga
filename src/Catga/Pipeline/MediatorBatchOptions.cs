using System;

namespace Catga.Pipeline;

public sealed record MediatorBatchOptions
{
    public bool EnableAutoBatching { get; set; } = false;
    public int MaxBatchSize { get; set; } = 100;
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    public int MaxQueueLength { get; set; } = 10_000;
    public TimeSpan ShardIdleTtl { get; set; } = TimeSpan.FromMinutes(2);
    public int MaxShards { get; set; } = 2048;
    public int FlushDegree { get; set; } = 0; // 0 => Sequential; >0 => limited concurrency within a batch
}
