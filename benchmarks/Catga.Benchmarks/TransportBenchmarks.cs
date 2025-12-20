using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Resilience;
using Catga.Transport;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using StackExchange.Redis;
using Testcontainers.Nats;
using Testcontainers.Redis;

namespace Catga.Benchmarks;

/// <summary>
/// Transport performance benchmarks using Testcontainers.
/// Compares InMemory, Redis, and NATS transports.
/// Run: dotnet run -c Release -- --filter *Transport*
/// 
/// NOTE: Requires Docker to be running!
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TransportBenchmarks
{
    private RedisContainer? _redisContainer;
    private NatsContainer? _natsContainer;

    private IServiceProvider _inMemoryProvider = null!;
    private IServiceProvider _redisProvider = null!;
    private IServiceProvider _natsProvider = null!;

    private IMessageTransport _inMemoryTransport = null!;
    private IMessageTransport _redisTransport = null!;
    private IMessageTransport _natsTransport = null!;

    private TxTestMessage _testMessage = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _testMessage = new TxTestMessage(1, "test data for transport benchmark");

        // Start containers
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redisContainer.StartAsync();

        _natsContainer = new NatsBuilder()
            .WithImage("nats:2-alpine")
            .Build();
        await _natsContainer.StartAsync();

        // Setup InMemory
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        inMemoryServices.AddCatga().UseMemoryPack();
        inMemoryServices.AddInMemoryTransport();
        inMemoryServices.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();
        _inMemoryProvider = inMemoryServices.BuildServiceProvider();
        _inMemoryTransport = _inMemoryProvider.GetRequiredService<IMessageTransport>();

        // Setup Redis
        var redisServices = new ServiceCollection();
        redisServices.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        redisServices.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString()));
        redisServices.AddCatga().UseMemoryPack();
        redisServices.AddRedisTransport();
        redisServices.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();
        _redisProvider = redisServices.BuildServiceProvider();
        _redisTransport = _redisProvider.GetRequiredService<IMessageTransport>();

        // Setup NATS
        var natsServices = new ServiceCollection();
        natsServices.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        natsServices.AddSingleton<INatsConnection>(new NatsConnection(new NatsOpts { Url = _natsContainer.GetConnectionString() }));
        natsServices.AddCatga().UseMemoryPack();
        natsServices.AddNatsTransport();
        natsServices.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();
        _natsProvider = natsServices.BuildServiceProvider();
        _natsTransport = _natsProvider.GetRequiredService<IMessageTransport>();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        (_inMemoryProvider as IDisposable)?.Dispose();
        (_redisProvider as IDisposable)?.Dispose();
        (_natsProvider as IDisposable)?.Dispose();

        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();
        if (_natsContainer != null)
            await _natsContainer.DisposeAsync();
    }

    #region Publish Single

    [Benchmark(Baseline = true, Description = "InMemory")]
    [BenchmarkCategory("Publish")]
    public Task InMemory_Publish()
        => _inMemoryTransport.PublishAsync(_testMessage);

    [Benchmark(Description = "Redis")]
    [BenchmarkCategory("Publish")]
    public Task Redis_Publish()
        => _redisTransport.PublishAsync(_testMessage);

    [Benchmark(Description = "NATS")]
    [BenchmarkCategory("Publish")]
    public Task Nats_Publish()
        => _natsTransport.PublishAsync(_testMessage);

    #endregion

    #region Publish Batch 100

    [Benchmark(Baseline = true, Description = "InMemory")]
    [BenchmarkCategory("Publish100")]
    public async Task InMemory_Publish100()
    {
        for (int i = 0; i < 100; i++)
            await _inMemoryTransport.PublishAsync(_testMessage);
    }

    [Benchmark(Description = "Redis")]
    [BenchmarkCategory("Publish100")]
    public async Task Redis_Publish100()
    {
        for (int i = 0; i < 100; i++)
            await _redisTransport.PublishAsync(_testMessage);
    }

    [Benchmark(Description = "NATS")]
    [BenchmarkCategory("Publish100")]
    public async Task Nats_Publish100()
    {
        for (int i = 0; i < 100; i++)
            await _natsTransport.PublishAsync(_testMessage);
    }

    #endregion

    #region Concurrent Publish 50

    [Benchmark(Baseline = true, Description = "InMemory")]
    [BenchmarkCategory("Concurrent50")]
    public async Task InMemory_Concurrent50()
    {
        var tasks = new Task[50];
        for (int i = 0; i < 50; i++)
            tasks[i] = _inMemoryTransport.PublishAsync(_testMessage);
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Redis")]
    [BenchmarkCategory("Concurrent50")]
    public async Task Redis_Concurrent50()
    {
        var tasks = new Task[50];
        for (int i = 0; i < 50; i++)
            tasks[i] = _redisTransport.PublishAsync(_testMessage);
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "NATS")]
    [BenchmarkCategory("Concurrent50")]
    public async Task Nats_Concurrent50()
    {
        var tasks = new Task[50];
        for (int i = 0; i < 50; i++)
            tasks[i] = _natsTransport.PublishAsync(_testMessage);
        await Task.WhenAll(tasks);
    }

    #endregion
}

[MemoryPackable]
public partial record TxTestMessage(int Id, string Data) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}
