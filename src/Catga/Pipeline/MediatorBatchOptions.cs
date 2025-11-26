using System;

namespace Catga.Pipeline;

public sealed record MediatorBatchOptions
{
    public bool EnableAutoBatching { get; init; } = false;
    public int MaxBatchSize { get; init; } = 100;
    public TimeSpan BatchTimeout { get; init; } = TimeSpan.FromMilliseconds(100);
    public int MaxQueueLength { get; init; } = 10_000;
}
