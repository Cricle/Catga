using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Catga;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmarks to measure Mediator performance optimizations
/// Compares: Handler caching, fast paths, object pooling
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MediatorOptimizationBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;

    private TestCommand _command = null!;
    private TestEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Add Catga with all optimizations enabled
        services.AddSingleton(new Configuration.CatgaOptions
        {
            EnableLogging = false,
            MaxConcurrentRequests = 0, // Disable to measure pure mediator
            EnableCircuitBreaker = false,
            EnableRateLimiting = false
        });

        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(NullLogger<CatgaMediator>.Instance);

        // Register handlers
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();

        _command = new TestCommand { Value = 42 };
        _event = new TestEvent { Message = "Test" };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    // Baseline: Simple command with handler cache
    [Benchmark(Baseline = true, Description = "SendAsync (optimized)")]
    public async Task<CatgaResult<TestResponse>> SendAsync_Optimized()
    {
        return await _mediator.SendAsync<TestCommand, TestResponse>(_command);
    }

    // Event publishing with handler cache + fast paths
    [Benchmark(Description = "PublishAsync (optimized)")]
    public async Task PublishAsync_Optimized()
    {
        await _mediator.PublishAsync(_event);
    }

    // Batch commands (measures handler cache effectiveness)
    [Benchmark(Description = "Batch Commands (1000x)")]
    public async Task BatchCommands_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _mediator.SendAsync<TestCommand, TestResponse>(_command);
        }
    }

    // Batch events (measures fast path effectiveness)
    [Benchmark(Description = "Batch Events (1000x)")]
    public async Task BatchEvents_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _mediator.PublishAsync(_event);
        }
    }

    // Test types
    public record TestCommand : IRequest<TestResponse>
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public int Value { get; init; }
    }

    public record TestResponse
    {
        public int Result { get; init; }
    }

    public record TestEvent : IEvent
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public string Message { get; init; } = string.Empty;
    }

    // Handlers
    public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(
            TestCommand request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CatgaResult<TestResponse>.Success(
                new TestResponse { Result = request.Value * 2 }));
        }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent notification, CancellationToken cancellationToken = default)
        {
            // Minimal work
            _ = notification.Message.Length;
            return Task.CompletedTask;
        }
    }
}

