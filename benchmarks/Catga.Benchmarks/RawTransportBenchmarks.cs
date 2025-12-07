using BenchmarkDotNet.Attributes;
using StackExchange.Redis;
using NATS.Client.Core;
using System.Text;
using System.Text.Json;

namespace Catga.Benchmarks;

/// <summary>
/// Raw transport performance benchmarks - measures actual Redis/NATS operations
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class RawTransportBenchmarks
{
    private ConnectionMultiplexer? _redis;
    private IDatabase? _redisDb;
    private NatsConnection? _nats;

    private readonly byte[] _smallPayload = Encoding.UTF8.GetBytes("{\"id\":1,\"name\":\"test\"}");
    private readonly byte[] _mediumPayload = new byte[1024]; // 1KB
    private readonly byte[] _largePayload = new byte[10240]; // 10KB

    [GlobalSetup]
    public async Task Setup()
    {
        // Initialize payloads
        Random.Shared.NextBytes(_mediumPayload);
        Random.Shared.NextBytes(_largePayload);

        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
            _redisDb = _redis.GetDatabase();
            Console.WriteLine("Redis connected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis not available: {ex.Message}");
        }

        try
        {
            _nats = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222" });
            await _nats.ConnectAsync();
            Console.WriteLine("NATS connected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NATS not available: {ex.Message}");
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _redis?.Dispose();
        if (_nats != null)
            await _nats.DisposeAsync();
    }

    #region Redis Benchmarks

    [Benchmark(Description = "Redis SET (small)")]
    public async Task Redis_Set_Small()
    {
        if (_redisDb == null) return;
        await _redisDb.StringSetAsync("bench:key", _smallPayload);
    }

    [Benchmark(Description = "Redis GET (small)")]
    public async Task<RedisValue> Redis_Get_Small()
    {
        if (_redisDb == null) return RedisValue.Null;
        return await _redisDb.StringGetAsync("bench:key");
    }

    [Benchmark(Description = "Redis SET+GET (small)")]
    public async Task<RedisValue> Redis_SetGet_Small()
    {
        if (_redisDb == null) return RedisValue.Null;
        await _redisDb.StringSetAsync("bench:key", _smallPayload);
        return await _redisDb.StringGetAsync("bench:key");
    }

    [Benchmark(Description = "Redis SET (1KB)")]
    public async Task Redis_Set_Medium()
    {
        if (_redisDb == null) return;
        await _redisDb.StringSetAsync("bench:key:1k", _mediumPayload);
    }

    [Benchmark(Description = "Redis SET (10KB)")]
    public async Task Redis_Set_Large()
    {
        if (_redisDb == null) return;
        await _redisDb.StringSetAsync("bench:key:10k", _largePayload);
    }

    [Benchmark(Description = "Redis PUBLISH")]
    public async Task<long> Redis_Publish()
    {
        if (_redis == null) return 0;
        var sub = _redis.GetSubscriber();
        return await sub.PublishAsync(RedisChannel.Literal("bench:channel"), _smallPayload);
    }

    [Benchmark(Description = "Redis Pipeline (10 ops)")]
    public async Task Redis_Pipeline_10()
    {
        if (_redisDb == null) return;
        var batch = _redisDb.CreateBatch();
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = batch.StringSetAsync($"bench:pipe:{i}", _smallPayload);
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Redis Pipeline (100 ops)")]
    public async Task Redis_Pipeline_100()
    {
        if (_redisDb == null) return;
        var batch = _redisDb.CreateBatch();
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = batch.StringSetAsync($"bench:pipe:{i}", _smallPayload);
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    #endregion

    #region NATS Benchmarks

    [Benchmark(Description = "NATS Publish (small)")]
    public async Task Nats_Publish_Small()
    {
        if (_nats == null) return;
        await _nats.PublishAsync("bench.subject", _smallPayload);
    }

    [Benchmark(Description = "NATS Publish (1KB)")]
    public async Task Nats_Publish_Medium()
    {
        if (_nats == null) return;
        await _nats.PublishAsync("bench.subject", _mediumPayload);
    }

    [Benchmark(Description = "NATS Publish (10KB)")]
    public async Task Nats_Publish_Large()
    {
        if (_nats == null) return;
        await _nats.PublishAsync("bench.subject", _largePayload);
    }

    [Benchmark(Description = "NATS Publish Batch (10)")]
    public async Task Nats_Publish_Batch_10()
    {
        if (_nats == null) return;
        for (int i = 0; i < 10; i++)
        {
            await _nats.PublishAsync($"bench.subject.{i}", _smallPayload);
        }
    }

    [Benchmark(Description = "NATS Publish Batch (100)")]
    public async Task Nats_Publish_Batch_100()
    {
        if (_nats == null) return;
        for (int i = 0; i < 100; i++)
        {
            await _nats.PublishAsync($"bench.subject.{i}", _smallPayload);
        }
    }

    #endregion

    #region Serialization Benchmarks

    private readonly TestMessage _testMessage = new("test-id", "Test Name", 12345, DateTime.UtcNow);

    [Benchmark(Baseline = true, Description = "JSON Serialize")]
    public byte[] Json_Serialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testMessage);
    }

    [Benchmark(Description = "JSON Deserialize")]
    public TestMessage? Json_Deserialize()
    {
        return JsonSerializer.Deserialize<TestMessage>(_smallPayload);
    }

    [Benchmark(Description = "JSON Round-trip")]
    public TestMessage? Json_Roundtrip()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testMessage);
        return JsonSerializer.Deserialize<TestMessage>(bytes);
    }

    #endregion
}

public record TestMessage(string Id, string Name, int Value, DateTime Timestamp);
