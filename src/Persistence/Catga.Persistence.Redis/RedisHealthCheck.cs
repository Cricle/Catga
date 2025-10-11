using Catga.HealthCheck;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Health check for Redis connectivity
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public string Name => "Redis";

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
    }

    public async ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var pingResult = await db.PingAsync();

            var data = new Dictionary<string, object>
            {
                ["connected"] = _redis.IsConnected,
                ["ping_ms"] = pingResult.TotalMilliseconds,
                ["endpoints"] = _redis.GetEndPoints().Length
            };

            if (_redis.IsConnected && pingResult.TotalMilliseconds < 100)
            {
                return HealthCheckResult.Healthy("Redis is connected and responsive", data);
            }

            if (_redis.IsConnected)
            {
                return HealthCheckResult.Degraded(
                    $"Redis is connected but slow (ping: {pingResult.TotalMilliseconds}ms)",
                    data);
            }

            return HealthCheckResult.Unhealthy("Redis is not connected", null, data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Failed to check Redis health: {ex.Message}",
                ex);
        }
    }
}

