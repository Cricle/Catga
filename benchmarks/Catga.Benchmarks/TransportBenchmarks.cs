using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using NATS.Client.Core;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmarks for Redis and NATS transport performance
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class TransportBenchmarks
{
    private IServiceProvider _inMemoryProvider = null!;
    private IServiceProvider _redisProvider = null!;
    private IServiceProvider _natsProvider = null!;

    private ICatgaMediator _inMemoryMediator = null!;
    private ICatgaMediator _redisMediator = null!;
    private ICatgaMediator _natsMediator = null!;

    private ConnectionMultiplexer? _redis;
    private NatsConnection? _nats;

    [GlobalSetup]
    public async Task Setup()
    {
        // InMemory setup
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        inMemoryServices.AddCatga();
        inMemoryServices.AddSingleton<IRequestHandler<TransportBenchCommand, TransportBenchResponse>, TransportBenchHandler>();
        _inMemoryProvider = inMemoryServices.BuildServiceProvider();
        _inMemoryMediator = _inMemoryProvider.GetRequiredService<ICatgaMediator>();

        // Redis setup
        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
            var redisServices = new ServiceCollection();
            redisServices.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            redisServices.AddCatga();
            redisServices.AddSingleton(_redis);
            redisServices.AddSingleton<IRequestHandler<TransportBenchCommand, TransportBenchResponse>, TransportBenchHandler>();
            _redisProvider = redisServices.BuildServiceProvider();
            _redisMediator = _redisProvider.GetRequiredService<ICatgaMediator>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis not available: {ex.Message}");
            _redisMediator = _inMemoryMediator; // Fallback
        }

        // NATS setup
        try
        {
            _nats = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222" });
            await _nats.ConnectAsync();
            var natsServices = new ServiceCollection();
            natsServices.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            natsServices.AddCatga();
            natsServices.AddSingleton(_nats);
            natsServices.AddSingleton<IRequestHandler<TransportBenchCommand, TransportBenchResponse>, TransportBenchHandler>();
            _natsProvider = natsServices.BuildServiceProvider();
            _natsMediator = _natsProvider.GetRequiredService<ICatgaMediator>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NATS not available: {ex.Message}");
            _natsMediator = _inMemoryMediator; // Fallback
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _redis?.Dispose();
        if (_nats != null)
            await _nats.DisposeAsync();
    }

    [Benchmark(Baseline = true, Description = "InMemory Send")]
    public async ValueTask<CatgaResult<TransportBenchResponse>> InMemory_Send()
    {
        return await _inMemoryMediator.SendAsync<TransportBenchCommand, TransportBenchResponse>(
            new TransportBenchCommand("test", 100));
    }

    [Benchmark(Description = "Redis Connected Send")]
    public async ValueTask<CatgaResult<TransportBenchResponse>> Redis_Send()
    {
        return await _redisMediator.SendAsync<TransportBenchCommand, TransportBenchResponse>(
            new TransportBenchCommand("test", 100));
    }

    [Benchmark(Description = "NATS Connected Send")]
    public async ValueTask<CatgaResult<TransportBenchResponse>> Nats_Send()
    {
        return await _natsMediator.SendAsync<TransportBenchCommand, TransportBenchResponse>(
            new TransportBenchCommand("test", 100));
    }

    [Benchmark(Description = "InMemory Batch 100")]
    public async Task<IReadOnlyList<CatgaResult<TransportBenchResponse>>> InMemory_Batch100()
    {
        var requests = Enumerable.Range(0, 100)
            .Select(i => new TransportBenchCommand($"test-{i}", i))
            .ToList();
        return await _inMemoryMediator.SendBatchAsync<TransportBenchCommand, TransportBenchResponse>(requests);
    }

    [Benchmark(Description = "Redis Batch 100")]
    public async Task<IReadOnlyList<CatgaResult<TransportBenchResponse>>> Redis_Batch100()
    {
        var requests = Enumerable.Range(0, 100)
            .Select(i => new TransportBenchCommand($"test-{i}", i))
            .ToList();
        return await _redisMediator.SendBatchAsync<TransportBenchCommand, TransportBenchResponse>(requests);
    }

    [Benchmark(Description = "NATS Batch 100")]
    public async Task<IReadOnlyList<CatgaResult<TransportBenchResponse>>> Nats_Batch100()
    {
        var requests = Enumerable.Range(0, 100)
            .Select(i => new TransportBenchCommand($"test-{i}", i))
            .ToList();
        return await _natsMediator.SendBatchAsync<TransportBenchCommand, TransportBenchResponse>(requests);
    }
}

public record TransportBenchCommand(string Name, int Value) : IRequest<TransportBenchResponse>
{
    public long MessageId => 0;
    public string? CorrelationId { get; set; }
}

public record TransportBenchResponse(string Result, int ProcessedValue);

public class TransportBenchHandler : IRequestHandler<TransportBenchCommand, TransportBenchResponse>
{
    public ValueTask<CatgaResult<TransportBenchResponse>> HandleAsync(
        TransportBenchCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TransportBenchResponse($"Processed: {request.Name}", request.Value * 2);
        return new ValueTask<CatgaResult<TransportBenchResponse>>(CatgaResult<TransportBenchResponse>.Success(response));
    }
}
