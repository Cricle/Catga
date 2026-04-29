using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Messaging;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.TDD;

// ── Shared types ──────────────────────────────────────────────────────────────

public record ProcessOrder(string OrderId) : IRequest<string> { public long MessageId { get; init; } }
public record SendEmail(string To, string Subject) : IRequest { public long MessageId { get; init; } }
public record OrderProcessed(string OrderId) : IEvent { public long MessageId { get; init; } }

// ═══════════════════════════════════════════════════════════════════════════════
// 1. FAULT MESSAGE TESTS (RED)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Fault<T> is published automatically when a handler throws or returns failure.
/// Consumers can subscribe to Fault<ProcessOrder> to handle errors.
/// Equivalent to MassTransit's Fault<T> pattern.
/// </summary>
public class FaultMessageTests
{
    private static void ClearCaches()
    {
        var t = typeof(CatgaMediator);
        (t.GetField("_handlerCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_behaviorCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
    }
    [Fact]
    public void Fault_GenericType_Exists()
    {
        // RED: Fault<T> does not exist yet
        typeof(Fault<>).Should().NotBeNull();
    }

    [Fact]
    public void Fault_HasExpectedProperties()
    {
        var fault = new Fault<ProcessOrder>(
            new ProcessOrder("ORD-1"),
            new InvalidOperationException("payment failed"),
            "HANDLER_FAILED");

        fault.Message.OrderId.Should().Be("ORD-1");
        fault.Exception!.Message.Should().Be("payment failed");
        fault.ErrorCode.Should().Be("HANDLER_FAILED");
        fault.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Fault_ImplementsIEvent()
    {
        typeof(Fault<ProcessOrder>).GetInterfaces()
            .Should().Contain(typeof(IEvent));
    }

    [Fact]
    public async Task FaultBehavior_WhenHandlerFails_PublishesFault()
    {
        ClearCaches();
        var faults = new List<Fault<ProcessOrder>>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(FaultPublishingBehavior<,>));
        services.AddSingleton<IRequestHandler<ProcessOrder, string>>(_ => new FailingOrderHandler());
        services.AddSingleton<IEventHandler<Fault<ProcessOrder>>>(_ => new FaultCapture<ProcessOrder>(faults));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<ProcessOrder, string>(new ProcessOrder("ORD-FAIL"));

        result.IsSuccess.Should().BeFalse();
        faults.Should().HaveCount(1);
        faults[0].Message.OrderId.Should().Be("ORD-FAIL");
    }

    [Fact]
    public async Task FaultBehavior_WhenHandlerSucceeds_NoFaultPublished()
    {
        ClearCaches();
        var faults = new List<Fault<ProcessOrder>>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(FaultPublishingBehavior<,>));
        services.AddSingleton<IRequestHandler<ProcessOrder, string>>(_ => new SuccessOrderHandler());
        services.AddSingleton<IEventHandler<Fault<ProcessOrder>>>(_ => new FaultCapture<ProcessOrder>(faults));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<ProcessOrder, string>(new ProcessOrder("ORD-OK"));

        result.IsSuccess.Should().BeTrue();
        faults.Should().BeEmpty();
    }

    private sealed class FailingOrderHandler : IRequestHandler<ProcessOrder, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(ProcessOrder request, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Failure(
                ErrorInfo.FromException(new InvalidOperationException("payment failed"), ErrorCodes.HandlerFailed)));
    }

    private sealed class SuccessOrderHandler : IRequestHandler<ProcessOrder, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(ProcessOrder request, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Success("done"));
    }

    private sealed class FaultCapture<T> : IEventHandler<Fault<T>> where T : IMessage
    {
        private readonly List<Fault<T>> _faults;
        public FaultCapture(List<Fault<T>> faults) => _faults = faults;
        public ValueTask HandleAsync(Fault<T> @event, CancellationToken ct = default)
        {
            _faults.Add(@event);
            return ValueTask.CompletedTask;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 2. CORRELATIONID PROPAGATION TESTS (RED)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// CorrelationId is automatically propagated from parent message to all
/// child messages (commands, events) sent/published during handler execution.
/// </summary>
public class CorrelationIdPropagationTests
{
    private static void ClearCaches()
    {
        var t = typeof(CatgaMediator);
        (t.GetField("_handlerCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_behaviorCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
    }
    [Fact]
    public void ICorrelationContext_InterfaceExists()
    {
        // RED: ICorrelationContext does not exist yet
        typeof(ICorrelationContext).Should().NotBeNull();
    }

    [Fact]
    public void CorrelationContext_SetAndGet_Works()
    {
        // RED: CorrelationContext does not exist yet
        var ctx = new CorrelationContext();
        ctx.Set(12345L);
        ctx.Current.Should().Be(12345L);
    }

    [Fact]
    public void CorrelationContext_Clear_ResetsToNull()
    {
        var ctx = new CorrelationContext();
        ctx.Set(999L);
        ctx.Clear();
        ctx.Current.Should().BeNull();
    }

    [Fact]
    public async Task CorrelationPropagationBehavior_PropagatesCorrelationId()
    {
        ClearCaches();
        long? capturedCorrelationId = null;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>));
        services.AddSingleton<IRequestHandler<ProcessOrder, string>>(sp =>
            new CapturingHandler(sp.GetRequiredService<ICorrelationContext>(),
                id => capturedCorrelationId = id));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var request = new ProcessOrder("ORD-CORR") { MessageId = 42L };
        await mediator.SendAsync<ProcessOrder, string>(request);

        capturedCorrelationId.Should().NotBeNull();
        capturedCorrelationId.Should().Be(42L);
    }

    [Fact]
    public async Task CorrelationPropagation_ChildMessages_InheritCorrelationId()
    {
        ClearCaches();
        long? childCorrelationId = null;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>));
        services.AddSingleton<IRequestHandler<ProcessOrder, string>>(sp =>
            new PublishingHandler(sp.GetRequiredService<ICatgaMediator>()));
        services.AddSingleton<IEventHandler<OrderProcessed>>(sp =>
            new CorrelationCapturingEventHandler(
                sp.GetRequiredService<ICorrelationContext>(),
                id => childCorrelationId = id));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var request = new ProcessOrder("ORD-CHILD") { MessageId = 100L };
        await mediator.SendAsync<ProcessOrder, string>(request);

        childCorrelationId.Should().NotBeNull();
        childCorrelationId.Should().Be(100L);
    }

    [Fact]
    public async Task CorrelationContext_IsIsolated_PerAsyncChain()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga().UseInMemory().WithCorrelationPropagation();
        services.AddInMemoryTransport();

        var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<ICorrelationContext>();

        long? captured1 = null;
        long? captured2 = null;

        // Two concurrent async chains — each has isolated AsyncLocal value
        await Task.WhenAll(
            Task.Run(async () => { ctx.Set(111L); await Task.Delay(10); captured1 = ctx.Current; }),
            Task.Run(async () => { ctx.Set(222L); await Task.Delay(10); captured2 = ctx.Current; }));

        // Each chain sees its own value
        captured1.Should().Be(111L);
        captured2.Should().Be(222L);
    }

    private sealed class CapturingHandler : IRequestHandler<ProcessOrder, string>
    {
        private readonly ICorrelationContext _ctx;
        private readonly Action<long?> _capture;
        public CapturingHandler(ICorrelationContext ctx, Action<long?> capture)
        { _ctx = ctx; _capture = capture; }
        public ValueTask<CatgaResult<string>> HandleAsync(ProcessOrder request, CancellationToken ct = default)
        {
            _capture(_ctx.Current);
            return ValueTask.FromResult(CatgaResult<string>.Success("ok"));
        }
    }

    private sealed class PublishingHandler : IRequestHandler<ProcessOrder, string>
    {
        private readonly ICatgaMediator _mediator;
        public PublishingHandler(ICatgaMediator mediator) => _mediator = mediator;
        public async ValueTask<CatgaResult<string>> HandleAsync(ProcessOrder request, CancellationToken ct = default)
        {
            await _mediator.PublishAsync(new OrderProcessed(request.OrderId), ct);
            return CatgaResult<string>.Success("ok");
        }
    }

    private sealed class CorrelationCapturingEventHandler : IEventHandler<OrderProcessed>
    {
        private readonly ICorrelationContext _ctx;
        private readonly Action<long?> _capture;
        public CorrelationCapturingEventHandler(ICorrelationContext ctx, Action<long?> capture)
        { _ctx = ctx; _capture = capture; }
        public ValueTask HandleAsync(OrderProcessed @event, CancellationToken ct = default)
        {
            _capture(_ctx.Current);
            return ValueTask.CompletedTask;
        }
    }
}
