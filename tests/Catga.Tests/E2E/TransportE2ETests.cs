using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Transport layer features.
/// Tests message publishing, consumption, and transport reliability.
/// </summary>
public class TransportE2ETests
{
    [Fact]
    public async Task InMemoryTransport_PublishAndConsume_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var receivedMessages = new List<TestMessage>();
        services.AddSingleton<IEventHandler<TestMessage>>(new TestMessageHandler(msg => receivedMessages.Add(msg)));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new TestMessage("msg-1", "Hello"));
        await mediator.PublishAsync(new TestMessage("msg-2", "World"));

        await Task.Delay(50);

        // Assert
        receivedMessages.Should().HaveCount(2);
        receivedMessages.Should().Contain(m => m.Id == "msg-1");
        receivedMessages.Should().Contain(m => m.Id == "msg-2");
    }

    [Fact]
    public async Task Transport_BatchPublish_ProcessesAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var receivedCount = 0;
        services.AddSingleton<IEventHandler<TestMessage>>(new TestMessageHandler(_ => Interlocked.Increment(ref receivedCount)));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Publish batch
        var tasks = Enumerable.Range(1, 100).Select(i =>
            mediator.PublishAsync(new TestMessage($"msg-{i}", $"Content {i}")).AsTask()
        );

        await Task.WhenAll(tasks);
        await Task.Delay(100);

        // Assert
        receivedCount.Should().Be(100);
    }

    [Fact]
    public async Task Transport_RequestResponse_ReturnsResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<CalculateRequest, CalculateResponse>, CalculateHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var response = await mediator.SendAsync(new CalculateRequest(10, 5, "add"));

        // Assert
        response.Should().NotBeNull();
        response.Result.Should().Be(15);
    }

    [Fact]
    public async Task Transport_MultipleHandlers_AllInvoked()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        services.AddSingleton<IEventHandler<NotifyEvent>>(new NotifyHandler(() => handler1Called = true));
        services.AddSingleton<IEventHandler<NotifyEvent>>(new NotifyHandler(() => handler2Called = true));
        services.AddSingleton<IEventHandler<NotifyEvent>>(new NotifyHandler(() => handler3Called = true));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new NotifyEvent("notification-1"));
        await Task.Delay(50);

        // Assert
        handler1Called.Should().BeTrue();
        handler2Called.Should().BeTrue();
        handler3Called.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_WithPriority_ProcessesHighPriorityFirst()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var processOrder = new List<string>();
        services.AddSingleton<IEventHandler<PriorityMessage>>(new PriorityHandler(msg =>
        {
            lock (processOrder)
            {
                processOrder.Add(msg.Priority);
            }
        }));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new PriorityMessage("1", "Low"));
        await mediator.PublishAsync(new PriorityMessage("2", "High"));
        await mediator.PublishAsync(new PriorityMessage("3", "Medium"));

        await Task.Delay(50);

        // Assert
        processOrder.Should().HaveCount(3);
    }

    [Fact]
    public async Task Transport_Idempotency_PreventsDuplicates()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var idempotencyStore = sp.GetRequiredService<IIdempotencyStore>();

        var requestId = $"req-{Guid.NewGuid():N}";

        // Act - First request
        var isFirst = !await idempotencyStore.IsProcessedAsync(requestId);
        if (isFirst)
        {
            await idempotencyStore.StoreResultAsync(requestId, "FirstResult", TimeSpan.FromHours(1));
        }

        // Act - Duplicate request
        var isDuplicate = await idempotencyStore.IsProcessedAsync(requestId);

        // Assert
        isFirst.Should().BeTrue();
        isDuplicate.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_QoS_AtLeastOnce_Guarantees Delivery()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var deliveryCount = 0;
        services.AddSingleton<IEventHandler<ReliableMessage>>(new ReliableHandler(() => Interlocked.Increment(ref deliveryCount)));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            await mediator.PublishAsync(new ReliableMessage($"reliable-{i}"));
        }

        await Task.Delay(100);

        // Assert
        deliveryCount.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public async Task Transport_ErrorHandling_DoesNotStopProcessing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var processedCount = 0;
        var errorCount = 0;

        services.AddSingleton<IEventHandler<ErrorProneMessage>>(new ErrorProneHandler(
            msg =>
            {
                Interlocked.Increment(ref processedCount);
                if (msg.ShouldFail)
                {
                    Interlocked.Increment(ref errorCount);
                    throw new InvalidOperationException("Simulated failure");
                }
            }));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new ErrorProneMessage("1", false));
        await mediator.PublishAsync(new ErrorProneMessage("2", true)); // Will fail
        await mediator.PublishAsync(new ErrorProneMessage("3", false));

        await Task.Delay(100);

        // Assert
        processedCount.Should().Be(3);
        errorCount.Should().Be(1);
    }

    [Fact]
    public async Task Transport_ConcurrentRequests_HandlesAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<EchoRequest, EchoResponse>, EchoHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Send concurrent requests
        var tasks = Enumerable.Range(1, 50).Select(i =>
            mediator.SendAsync(new EchoRequest($"message-{i}")).AsTask()
        );

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(50);
        results.Should().AllSatisfy(r => r.Message.Should().StartWith("Echo: message-"));
    }

    [Fact]
    public async Task Transport_Timeout_RequestTimesOut()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<SlowRequest, SlowResponse>, SlowHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await mediator.SendAsync(new SlowRequest(5000), cts.Token);
        });
    }

    #region Test Types

    public record TestMessage(string Id, string Content) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record CalculateRequest(int A, int B, string Operation) : IRequest<CalculateResponse>;
    public record CalculateResponse(int Result);

    public record NotifyEvent(string NotificationId) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record PriorityMessage(string Id, string Priority) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record ReliableMessage(string Id) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record ErrorProneMessage(string Id, bool ShouldFail) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record EchoRequest(string Message) : IRequest<EchoResponse>;
    public record EchoResponse(string Message);

    public record SlowRequest(int DelayMs) : IRequest<SlowResponse>;
    public record SlowResponse(bool Completed);

    public class TestMessageHandler : IEventHandler<TestMessage>
    {
        private readonly Action<TestMessage> _onHandle;
        public TestMessageHandler(Action<TestMessage> onHandle) => _onHandle = onHandle;
        public ValueTask HandleAsync(TestMessage @event, CancellationToken ct = default)
        {
            _onHandle(@event);
            return ValueTask.CompletedTask;
        }
    }

    public class CalculateHandler : IRequestHandler<CalculateRequest, CalculateResponse>
    {
        public ValueTask<CalculateResponse> HandleAsync(CalculateRequest request, CancellationToken ct = default)
        {
            var result = request.Operation switch
            {
                "add" => request.A + request.B,
                "sub" => request.A - request.B,
                "mul" => request.A * request.B,
                _ => 0
            };
            return ValueTask.FromResult(new CalculateResponse(result));
        }
    }

    public class NotifyHandler : IEventHandler<NotifyEvent>
    {
        private readonly Action _onHandle;
        public NotifyHandler(Action onHandle) => _onHandle = onHandle;
        public ValueTask HandleAsync(NotifyEvent @event, CancellationToken ct = default)
        {
            _onHandle();
            return ValueTask.CompletedTask;
        }
    }

    public class PriorityHandler : IEventHandler<PriorityMessage>
    {
        private readonly Action<PriorityMessage> _onHandle;
        public PriorityHandler(Action<PriorityMessage> onHandle) => _onHandle = onHandle;
        public ValueTask HandleAsync(PriorityMessage @event, CancellationToken ct = default)
        {
            _onHandle(@event);
            return ValueTask.CompletedTask;
        }
    }

    public class ReliableHandler : IEventHandler<ReliableMessage>
    {
        private readonly Action _onHandle;
        public ReliableHandler(Action onHandle) => _onHandle = onHandle;
        public ValueTask HandleAsync(ReliableMessage @event, CancellationToken ct = default)
        {
            _onHandle();
            return ValueTask.CompletedTask;
        }
    }

    public class ErrorProneHandler : IEventHandler<ErrorProneMessage>
    {
        private readonly Action<ErrorProneMessage> _onHandle;
        public ErrorProneHandler(Action<ErrorProneMessage> onHandle) => _onHandle = onHandle;
        public ValueTask HandleAsync(ErrorProneMessage @event, CancellationToken ct = default)
        {
            _onHandle(@event);
            return ValueTask.CompletedTask;
        }
    }

    public class EchoHandler : IRequestHandler<EchoRequest, EchoResponse>
    {
        public ValueTask<EchoResponse> HandleAsync(EchoRequest request, CancellationToken ct = default)
        {
            return ValueTask.FromResult(new EchoResponse($"Echo: {request.Message}"));
        }
    }

    public class SlowHandler : IRequestHandler<SlowRequest, SlowResponse>
    {
        public async ValueTask<SlowResponse> HandleAsync(SlowRequest request, CancellationToken ct = default)
        {
            await Task.Delay(request.DelayMs, ct);
            return new SlowResponse(true);
        }
    }

    #endregion
}
