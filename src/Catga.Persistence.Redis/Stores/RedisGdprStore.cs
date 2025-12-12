using Catga.Abstractions;
using Catga.EventSourcing;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis-based GDPR store for managing data erasure requests.
/// </summary>
public sealed class RedisGdprStore : IGdprStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly string _keyPrefix;

    public RedisGdprStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string keyPrefix = "gdpr")
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
    }

    public async ValueTask SaveRequestAsync(ErasureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var db = _redis.GetDatabase();
        var key = BuildKey(request.SubjectId);
        var data = _serializer.Serialize(request);

        await db.StringSetAsync(key, data);
    }

    public async ValueTask<ErasureRequest?> GetErasureRequestAsync(string subjectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjectId);

        var db = _redis.GetDatabase();
        var key = BuildKey(subjectId);
        var data = await db.StringGetAsync(key);

        if (!data.HasValue)
            return null;

        return _serializer.Deserialize<ErasureRequest>((byte[])data);
    }

    public async ValueTask<IReadOnlyList<ErasureRequest>> GetPendingRequestsAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());

        var requests = new List<ErasureRequest>();
        var pattern = $"{_keyPrefix}:*";

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            var data = await db.StringGetAsync(key);
            if (data.HasValue)
            {
                var request = _serializer.Deserialize<ErasureRequest>((byte[])data);
                if (request != null)
                    requests.Add(request);
            }
        }

        return requests;
    }

    public async ValueTask DeleteRequestAsync(string subjectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjectId);

        var db = _redis.GetDatabase();
        var key = BuildKey(subjectId);

        await db.KeyDeleteAsync(key);
    }

    private string BuildKey(string subjectId) => $"{_keyPrefix}:{subjectId}";
}
