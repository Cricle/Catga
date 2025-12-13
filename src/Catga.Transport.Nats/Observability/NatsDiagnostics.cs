using System.Diagnostics.Metrics;

namespace Catga.Transport.Nats.Observability;

/// <summary>
/// NATS-specific diagnostics metrics.
/// </summary>
internal static class NatsDiagnostics
{
    public const string MeterName = "Catga.Transport.Nats";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> NatsDedupDrops = Meter.CreateCounter<long>(
        "catga.nats.dedup.drops", "messages", "Duplicates dropped by NATS transport deduplication");

    public static readonly Counter<long> NatsDedupEvictions = Meter.CreateCounter<long>(
        "catga.nats.dedup.evictions", "items", "Dedup cache evictions in NATS transport");
}
