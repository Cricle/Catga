using Catga.Debugging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis debug metadata extractor - minimal overhead
/// </summary>
public static class RedisDebugMetadata
{
    /// <summary>
    /// Extract metadata from Redis operation - no extra allocations
    /// </summary>
    public static void ExtractMetadata(string key, RedisValue value, Dictionary<string, string> metadata)
    {
        // Redis key pattern
        metadata["redis.key"] = key;

        // Value size (no ToString() to avoid allocation)
        if (value.HasValue)
        {
            metadata["redis.value_size"] = value.Length().ToString();
        }

        // Extract key components (zero-copy substring)
        var keySpan = key.AsSpan();
        var colonIndex = keySpan.IndexOf(':');
        if (colonIndex > 0)
        {
            metadata["redis.key_type"] = keySpan.Slice(0, colonIndex).ToString();
        }
    }

    /// <summary>
    /// Extract metadata from Redis command - reuse existing data
    /// </summary>
    public static void ExtractCommandMetadata(string command, Dictionary<string, string> metadata)
    {
        metadata["redis.command"] = command;
        
        // Command type classification (no allocations)
        metadata["redis.operation"] = command switch
        {
            "GET" or "MGET" or "HGET" or "HGETALL" => "read",
            "SET" or "MSET" or "HSET" or "HMSET" => "write",
            "DEL" or "HDEL" => "delete",
            "PUBLISH" => "pubsub",
            _ => "other"
        };
    }
}

