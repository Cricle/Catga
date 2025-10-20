using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis batch operations for high-performance bulk operations
/// Reduces network round-trips by using Redis Pipeline
/// </summary>
public class RedisBatchOperations
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisBatchOperations(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = redis.GetDatabase();
    }

    /// <summary>
    /// Batch set multiple key-value pairs (uses Pipeline for 1 round-trip)
    /// </summary>
    public async Task BatchSetAsync(
        IDictionary<string, string> keyValuePairs,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task>(keyValuePairs.Count);

        foreach (var kvp in keyValuePairs)
        {
            tasks.Add(batch.StringSetAsync(kvp.Key, kvp.Value, expiry));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Batch get multiple values (uses Pipeline for 1 round-trip)
    /// </summary>
    public async Task<IDictionary<string, string?>> BatchGetAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var batch = _db.CreateBatch();
        var tasks = new Dictionary<string, Task<RedisValue>>();

        foreach (var key in keys)
        {
            tasks[key] = batch.StringGetAsync(key);
        }

        batch.Execute();
        await Task.WhenAll(tasks.Values);

        var result = new Dictionary<string, string?>();
        foreach (var kvp in tasks)
        {
            var value = await kvp.Value;
            result[kvp.Key] = value.HasValue ? value.ToString() : null;
        }

        return result;
    }

    /// <summary>
    /// Batch delete multiple keys (uses Pipeline for 1 round-trip)
    /// </summary>
    public async Task<long> BatchDeleteAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task<bool>>();

        foreach (var key in keys)
        {
            tasks.Add(batch.KeyDeleteAsync(key));
        }

        batch.Execute();
        var results = await Task.WhenAll(tasks);

        // Manual count instead of LINQ Count()
        var count = 0;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i])
                count++;
        }
        return count;
    }

    /// <summary>
    /// Batch hash set (for complex objects)
    /// </summary>
    public async Task BatchHashSetAsync(
        string hashKey,
        IDictionary<string, string> fields,
        CancellationToken cancellationToken = default)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task>();

        foreach (var field in fields)
        {
            tasks.Add(batch.HashSetAsync(hashKey, field.Key, field.Value));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Batch list operations (for queues)
    /// </summary>
    public async Task<long> BatchListPushAsync(
        string listKey,
        IEnumerable<string> values,
        CancellationToken cancellationToken = default)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task<long>>();

        foreach (var value in values)
        {
            tasks.Add(batch.ListRightPushAsync(listKey, value));
        }

        batch.Execute();
        var results = await Task.WhenAll(tasks);

        // Return the last result (final list length) without blocking .Result
        return results.Length > 0 ? results[^1] : 0;
    }
}

