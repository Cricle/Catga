using System.Collections.Concurrent;

namespace Catga.Idempotency;

/// <summary>Typed idempotency store cache (no Type comparisons, uses byte[] for serialized data)</summary>
internal static class TypedIdempotencyCache<TResult>
{
    public static readonly ConcurrentDictionary<long, (DateTime Timestamp, byte[] Data)> Cache = new();
}

