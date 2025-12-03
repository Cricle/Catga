using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Idempotency;
using Catga.Pipeline;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed partial class MediatorE2ETests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        if (_redisContainer is not null)
            await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Mediator_SendCommand_ReturnsResult()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResult>, CreateOrderHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new CreateOrderCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            CustomerId = "customer-1",
            Amount = 150.00m
        };

        var result = await mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.OrderId.Should().NotBeNullOrEmpty();
        result.Value.Status.Should().Be("Created");
    }

    [Fact]
    public async Task Mediator_PublishEvent_AllHandlersReceive()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<OrderPlacedEvent>, OrderPlacedHandler1>();
        services.AddScoped<IEventHandler<OrderPlacedEvent>, OrderPlacedHandler2>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        OrderPlacedHandler1.ReceivedCount = 0;
        OrderPlacedHandler2.ReceivedCount = 0;

        var @event = new OrderPlacedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = "order-1",
            Amount = 200.00m
        };

        await mediator.PublishAsync(@event);

        OrderPlacedHandler1.ReceivedCount.Should().Be(1);
        OrderPlacedHandler2.ReceivedCount.Should().Be(1);
    }

    [Fact]
    public async Task Mediator_WithIdempotency_DeduplicatesRequests()
    {
        if (_redis is null) return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_redis);
        services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
        services.AddCatga()
            .UseResilience();
        services.AddRedisIdempotencyStore();
        services.AddScoped<IRequestHandler<IdempotentCommand, IdempotentResult>, IdempotentHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var idempotencyStore = sp.GetRequiredService<IIdempotencyStore>();

        IdempotentHandler.ExecutionCount = 0;

        var messageId = MessageExtensions.NewMessageId();
        var command = new IdempotentCommand { MessageId = messageId, Data = "test" };

        // First execution
        var result1 = await mediator.SendAsync<IdempotentCommand, IdempotentResult>(command);
        result1.IsSuccess.Should().BeTrue();

        // Mark as processed
        await idempotencyStore.MarkAsProcessedAsync(messageId, result1.Value!);

        // Second execution with same ID should return cached result
        var hasProcessed = await idempotencyStore.HasBeenProcessedAsync(messageId);
        hasProcessed.Should().BeTrue();

        var cached = await idempotencyStore.GetCachedResultAsync<IdempotentResult>(messageId);
        cached.Should().NotBeNull();
        cached!.Value.Should().Be(result1.Value!.Value);
    }

    [Fact]
    public async Task Mediator_ConcurrentRequests_AllSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ConcurrentCommand, ConcurrentResult>, ConcurrentHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            var command = new ConcurrentCommand { MessageId = MessageExtensions.NewMessageId(), Index = i };
            return await mediator.SendAsync<ConcurrentCommand, ConcurrentResult>(command);
        });

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Select(r => r.Value!.ProcessedIndex).Should().BeEquivalentTo(Enumerable.Range(0, 50));
    }

    [Fact]
    public async Task Mediator_WithPipeline_ExecutesBehaviors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<PipelineCommand, PipelineResult>, PipelineHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        LoggingBehavior<PipelineCommand, PipelineResult>.BeforeCount = 0;
        LoggingBehavior<PipelineCommand, PipelineResult>.AfterCount = 0;

        var command = new PipelineCommand { MessageId = MessageExtensions.NewMessageId(), Data = "pipeline-test" };
        var result = await mediator.SendAsync<PipelineCommand, PipelineResult>(command);

        result.IsSuccess.Should().BeTrue();
        LoggingBehavior<PipelineCommand, PipelineResult>.BeforeCount.Should().Be(1);
        LoggingBehavior<PipelineCommand, PipelineResult>.AfterCount.Should().Be(1);
    }

    #region Commands and Handlers

    [MemoryPackable]
    private partial record CreateOrderCommand : IRequest<CreateOrderResult>
    {
        public required long MessageId { get; init; }
        public required string CustomerId { get; init; }
        public required decimal Amount { get; init; }
    }

    [MemoryPackable]
    private partial record CreateOrderResult
    {
        public required string OrderId { get; init; }
        public required string Status { get; init; }
    }

    private sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
    {
        public Task<CatgaResult<CreateOrderResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
        {
            var result = new CreateOrderResult { OrderId = $"ORD-{Guid.NewGuid():N}", Status = "Created" };
            return Task.FromResult(CatgaResult<CreateOrderResult>.Success(result));
        }
    }

    [MemoryPackable]
    private partial record OrderPlacedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required decimal Amount { get; init; }
    }

    private sealed class OrderPlacedHandler1 : IEventHandler<OrderPlacedEvent>
    {
        public static int ReceivedCount;
        public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return Task.CompletedTask;
        }
    }

    private sealed class OrderPlacedHandler2 : IEventHandler<OrderPlacedEvent>
    {
        public static int ReceivedCount;
        public Task HandleAsync(OrderPlacedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return Task.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record IdempotentCommand : IRequest<IdempotentResult>
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }

    [MemoryPackable]
    private partial record IdempotentResult
    {
        public required string Value { get; init; }
    }

    private sealed class IdempotentHandler : IRequestHandler<IdempotentCommand, IdempotentResult>
    {
        public static int ExecutionCount;
        public Task<CatgaResult<IdempotentResult>> HandleAsync(IdempotentCommand request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExecutionCount);
            return Task.FromResult(CatgaResult<IdempotentResult>.Success(new IdempotentResult { Value = $"processed-{request.Data}" }));
        }
    }

    [MemoryPackable]
    private partial record ConcurrentCommand : IRequest<ConcurrentResult>
    {
        public required long MessageId { get; init; }
        public required int Index { get; init; }
    }

    [MemoryPackable]
    private partial record ConcurrentResult
    {
        public required int ProcessedIndex { get; init; }
    }

    private sealed class ConcurrentHandler : IRequestHandler<ConcurrentCommand, ConcurrentResult>
    {
        public async Task<CatgaResult<ConcurrentResult>> HandleAsync(ConcurrentCommand request, CancellationToken ct = default)
        {
            await Task.Delay(10, ct); // Simulate work
            return CatgaResult<ConcurrentResult>.Success(new ConcurrentResult { ProcessedIndex = request.Index });
        }
    }

    [MemoryPackable]
    private partial record PipelineCommand : IRequest<PipelineResult>
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }

    [MemoryPackable]
    private partial record PipelineResult
    {
        public required string Result { get; init; }
    }

    private sealed class PipelineHandler : IRequestHandler<PipelineCommand, PipelineResult>
    {
        public Task<CatgaResult<PipelineResult>> HandleAsync(PipelineCommand request, CancellationToken ct = default)
        {
            return Task.FromResult(CatgaResult<PipelineResult>.Success(new PipelineResult { Result = $"handled-{request.Data}" }));
        }
    }

    private sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public static int BeforeCount;
        public static int AfterCount;

        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            Interlocked.Increment(ref BeforeCount);
            var result = await next();
            Interlocked.Increment(ref AfterCount);
            return result;
        }
    }

    #endregion
}
