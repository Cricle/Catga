using System.Collections.Concurrent;

namespace Catga.Idempotency;

/// <summary>Typed idempotency store cache (no Type comparisons)</summary>
internal static class TypedIdempotencyCache<TResult>
{
    public static readonly ConcurrentDictionary<string, (DateTime Timestamp, string Json)> Cache = new();
}

