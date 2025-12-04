using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Locking;

/// <summary>Redis-backed leader election.</summary>
public sealed partial class RedisLeaderElection : ILeaderElection, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly LeaderElectionOptions _opts;
    private readonly ILogger<RedisLeaderElection> _logger;
    private readonly Timer _renewTimer;
    private readonly Dictionary<string, Handle> _active = [];
    private readonly Lock _lock = new();

    public RedisLeaderElection(IConnectionMultiplexer redis, IOptions<LeaderElectionOptions> options, ILogger<RedisLeaderElection> logger)
    {
        _redis = redis;
        _opts = options.Value;
        _logger = logger;
        _renewTimer = new(RenewLeaderships, null, _opts.RenewInterval, _opts.RenewInterval);
    }

    public async ValueTask<ILeadershipHandle?> TryAcquireLeadershipAsync(
        string electionId,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _opts.KeyPrefix + electionId;
        var leaderValue = BuildLeaderValue();
        var expiry = _opts.LeaseDuration;

        // Try to set if not exists
        var acquired = await db.StringSetAsync(
            key,
            leaderValue,
            expiry,
            When.NotExists);

        if (acquired)
        {
            LogLeadershipAcquired(_logger, electionId, _opts.NodeId);
            var handle = new Handle(this, electionId, _opts.NodeId, DateTimeOffset.UtcNow);

            lock (_lock)
            {
                _active[electionId] = handle;
            }

            return handle;
        }

        // Check if we already hold leadership (re-entrant)
        var current = await db.StringGetAsync(key);
        if (current.HasValue && current.ToString().StartsWith(_opts.NodeId + "|"))
        {
            // Extend our existing leadership
            await db.KeyExpireAsync(key, expiry);

            lock (_lock)
            {
                if (_active.TryGetValue(electionId, out var existing))
                    return existing;
            }
        }

        return null;
    }

    public async ValueTask<ILeadershipHandle> AcquireLeadershipAsync(
        string electionId,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var retryDelay = TimeSpan.FromMilliseconds(500);
        var maxRetries = (int)(timeout.TotalMilliseconds / retryDelay.TotalMilliseconds);

        var pipeline = new ResiliencePipelineBuilder<ILeadershipHandle?>()
            .AddRetry(new RetryStrategyOptions<ILeadershipHandle?>
            {
                MaxRetryAttempts = Math.Max(1, maxRetries),
                Delay = retryDelay,
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<ILeadershipHandle?>().HandleResult(h => h is null)
            })
            .Build();

        var handle = await pipeline.ExecuteAsync(async c => await TryAcquireLeadershipAsync(electionId, c), ct);

        if (handle is null)
        {
            throw new TimeoutException($"Failed to acquire leadership for '{electionId}' within {timeout}");
        }

        return handle;
    }

    public async ValueTask<bool> IsLeaderAsync(string electionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _opts.KeyPrefix + electionId;

        var current = await db.StringGetAsync(key);
        return current.HasValue && current.ToString().StartsWith(_opts.NodeId + "|");
    }

    public async ValueTask<LeaderInfo?> GetLeaderAsync(string electionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _opts.KeyPrefix + electionId;

        var current = await db.StringGetAsync(key);
        if (!current.HasValue)
            return null;

        return ParseLeaderValue(current.ToString());
    }

    public async IAsyncEnumerable<LeadershipChange> WatchAsync(
        string electionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _opts.KeyPrefix + electionId;
        LeaderInfo? lastLeader = null;

        while (!ct.IsCancellationRequested)
        {
            var current = await db.StringGetAsync(key);
            LeaderInfo? currentLeader = current.HasValue ? ParseLeaderValue(current.ToString()) : null;

            if (!Equals(lastLeader, currentLeader))
            {
                var changeType = (lastLeader, currentLeader) switch
                {
                    (null, not null) => LeadershipChangeType.Elected,
                    (not null, null) => LeadershipChangeType.Lost,
                    _ => LeadershipChangeType.Elected // Leader changed
                };

                yield return new LeadershipChange
                {
                    Type = changeType,
                    PreviousLeader = lastLeader,
                    NewLeader = currentLeader,
                    Timestamp = DateTimeOffset.UtcNow
                };

                lastLeader = currentLeader;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    internal async ValueTask ResignAsync(string electionId)
    {
        var db = _redis.GetDatabase();
        var key = _opts.KeyPrefix + electionId;

        // Only delete if we are the leader
        var script = """
            local current = redis.call('GET', KEYS[1])
            if current and string.find(current, ARGV[1], 1, true) == 1 then
                return redis.call('DEL', KEYS[1])
            end
            return 0
            """;

        await db.ScriptEvaluateAsync(script, new RedisKey[] { key }, new RedisValue[] { _opts.NodeId + "|" });

        lock (_lock)
        {
            _active.Remove(electionId);
        }

        LogLeadershipResigned(_logger, electionId, _opts.NodeId);
    }

    internal async ValueTask ExtendAsync(string electionId)
    {
        var db = _redis.GetDatabase();
        var key = _opts.KeyPrefix + electionId;

        // Only extend if we are the leader
        var script = """
            local current = redis.call('GET', KEYS[1])
            if current and string.find(current, ARGV[1], 1, true) == 1 then
                return redis.call('PEXPIRE', KEYS[1], ARGV[2])
            end
            return 0
            """;

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key },
            new RedisValue[] { _opts.NodeId + "|", (long)_opts.LeaseDuration.TotalMilliseconds });

        if ((int)result == 0)
        {
            // Lost leadership
            lock (_lock)
            {
                if (_active.TryGetValue(electionId, out var handle))
                {
                    handle.MarkLost();
                    _active.Remove(electionId);
                }
            }
        }
    }

    private void RenewLeaderships(object? state)
    {
        List<string> elections;
        lock (_lock)
        {
            elections = _active.Keys.ToList();
        }

        foreach (var electionId in elections)
        {
            _ = ExtendAsync(electionId);
        }
    }

    private string BuildLeaderValue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{_opts.NodeId}|{timestamp}|{_opts.Endpoint ?? ""}";
    }

    private static LeaderInfo ParseLeaderValue(string value)
    {
        var parts = value.Split('|');
        return new LeaderInfo
        {
            NodeId = parts[0],
            AcquiredAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[1])),
            Endpoint = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : null
        };
    }

    public void Dispose()
    {
        _renewTimer.Dispose();

        // Resign all leaderships
        lock (_lock)
        {
            foreach (var electionId in _active.Keys.ToList())
            {
                _ = ResignAsync(electionId);
            }
        }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Leadership acquired: {ElectionId} (node: {NodeId})")]
    private static partial void LogLeadershipAcquired(ILogger logger, string electionId, string nodeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Leadership resigned: {ElectionId} (node: {NodeId})")]
    private static partial void LogLeadershipResigned(ILogger logger, string electionId, string nodeId);

    #endregion

    private sealed class Handle : ILeadershipHandle
    {
        private readonly RedisLeaderElection _parent;
        private bool _isLeader = true;

        public string ElectionId { get; }
        public string NodeId { get; }
        public DateTimeOffset AcquiredAt { get; }
        public bool IsLeader => _isLeader;

        public event Action? OnLeadershipLost;

        public Handle(
            RedisLeaderElection parent,
            string electionId,
            string nodeId,
            DateTimeOffset acquiredAt)
        {
            _parent = parent;
            ElectionId = electionId;
            NodeId = nodeId;
            AcquiredAt = acquiredAt;
        }

        public async ValueTask ExtendAsync(CancellationToken ct = default)
        {
            await _parent.ExtendAsync(ElectionId);
        }

        public async ValueTask DisposeAsync()
        {
            if (_isLeader)
            {
                await _parent.ResignAsync(ElectionId);
                _isLeader = false;
            }
        }

        internal void MarkLost()
        {
            _isLeader = false;
            OnLeadershipLost?.Invoke();
        }
    }
}
