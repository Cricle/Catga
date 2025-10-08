using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark for P3-1: HandlerCache 3-tier optimization
/// Tests the impact of ThreadLocal cache (L1)
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class HandlerCacheBenchmark
{
    private IServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestRequest, TestResponse>, TestRequestHandler>();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    /// <summary>
    /// Single request - benefits from L1 (ThreadLocal) cache on repeated calls
    /// </summary>
    [Benchmark]
    public async Task<CatgaResult<TestResponse>> SendAsync_SingleRequest()
    {
        return await _mediator.SendAsync<TestRequest, TestResponse>(new TestRequest("test"));
    }

    /// <summary>
    /// Sequential requests - demonstrates L1 cache effectiveness
    /// </summary>
    [Benchmark]
    [Arguments(10)]
    public async Task<CatgaResult<TestResponse>[]> SendAsync_Sequential(int count)
    {
        var results = new CatgaResult<TestResponse>[count];
        for (int i = 0; i < count; i++)
        {
            results[i] = await _mediator.SendAsync<TestRequest, TestResponse>(new TestRequest($"test{i}"));
        }
        return results;
    }

    /// <summary>
    /// Publish event - tests event handler cache
    /// </summary>
    [Benchmark]
    public async Task PublishAsync_Event()
    {
        await _mediator.PublishAsync(new TestEvent("test"));
    }

    // Test types
    public record TestRequest(string Data) : IRequest<TestResponse>
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public record TestResponse(string Result);

    public record TestEvent(string Data) : IEvent
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public class TestRequestHandler : IRequestHandler<TestRequest, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(
            TestRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CatgaResult<TestResponse>.Success(new TestResponse("OK")));
        }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

