using System;

namespace Catga.Resilience;

public sealed class CatgaResilienceOptions
{
    public TimeSpan MediatorTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public int MediatorBulkheadConcurrency { get; set; } = Math.Max(Environment.ProcessorCount * 2, 16);
    public int MediatorBulkheadQueueLimit { get; set; } = Math.Max(Environment.ProcessorCount * 2, 16);

    public TimeSpan TransportTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public int TransportRetryCount { get; set; } = 3;
    public TimeSpan TransportRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public int TransportBulkheadConcurrency { get; set; } = Math.Max(Environment.ProcessorCount * 4, 32);
    public int TransportBulkheadQueueLimit { get; set; } = Math.Max(Environment.ProcessorCount * 4, 32);

    public TimeSpan PersistenceTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public int PersistenceRetryCount { get; set; } = 3;
    public TimeSpan PersistenceRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public int PersistenceBulkheadConcurrency { get; set; } = 0;
    public int PersistenceBulkheadQueueLimit { get; set; } = 0;
}
