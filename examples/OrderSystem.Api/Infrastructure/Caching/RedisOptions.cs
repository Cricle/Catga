namespace OrderSystem.Api.Infrastructure.Caching;

public class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "OrderSystem:";
    public int SyncTimeout { get; set; } = 5000;
    public int ConnectRetry { get; set; } = 3;
    public int ConnectTimeout { get; set; } = 5000;
}
