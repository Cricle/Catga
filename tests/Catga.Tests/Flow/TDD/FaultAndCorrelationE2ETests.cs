using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.TDD;

// ── Domain types ──────────────────────────────────────────────────────────────

public record CreatePayment(string PaymentId, decimal Amount) : IRequest<string>
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
}
public record NotifyUser(string UserId, string Message) : IRequest
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
}
public record PaymentCreated(string PaymentId) : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
}
public record PaymentFailedEvt(string PaymentId, string Reason) : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// E2E: FAULT MESSAGE SCENARIOS
// ═══════════════════════════════════════════════════════════════════════════════

public class FaultMessageE2ETests
{
    private static void ClearCaches()
    {
        var t = typeof(CatgaMediator);
        (t.GetField("_handlerCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_behaviorCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
    }

    private static ServiceProvider BuildSp(
        Func<CreatePayment, CatgaResult<string>> handler,
        List<Fault<CreatePayment>>? faultCapture = null)
    {
        ClearCaches();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(FaultPublishingBehavior<,>));
        services.AddSingleton<IRequestHandler<CreatePayment, string>>(
            _ => new LambdaHandler<CreatePayment, string>(handler));
        if (faultCapture != null)
            services.AddSingleton<IEventHandler<Fault<CreatePayment>>>(
                _ => new FaultCapture<CreatePayment>(faultCapture));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Fault_HandlerReturnsFailure_FaultPublished()
    {
        var faults = new List<Fault<CreatePayment>>();
        await using var sp = BuildSp(
            _ => CatgaResult<string>.Failure(ErrorInfo.FromException(
                new InvalidOperationException("insufficient funds"), ErrorCodes.HandlerFailed)),
            faults);

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var result = await mediator.SendAsync<CreatePayment, string>(
            new CreatePayment("PAY-001", 500m));

        result.IsSuccess.Should().BeFalse();
        faults.Should().HaveCount(1);
        faults[0].Message.PaymentId.Should().Be("PAY-001");
        faults[0].ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
        faults[0].OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        faults[0].Host.Should().Be(Environment.MachineName);
    }

    [Fact]
    public async Task Fault_HandlerSucceeds_NoFaultPublished()
    {
        var faults = new List<Fault<CreatePayment>>();
        await using var sp = BuildSp(_ => CatgaResult<string>.Success("PAY-OK"), faults);

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        await mediator.SendAsync<CreatePayment, string>(new CreatePayment("PAY-002", 100m));

        faults.Should().BeEmpty();
    }

    [Fact]
    public async Task Fault_MultipleFailures_EachPublishesFault()
    {
        var faults = new List<Fault<CreatePayment>>();
        await using var sp = BuildSp(
            req => CatgaResult<string>.Failure($"failed: {req.PaymentId}"),
            faults);

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        await mediator.SendAsync<CreatePayment, string>(new CreatePayment("PAY-A", 10m));
        await mediator.SendAsync<CreatePayment, string>(new CreatePayment("PAY-B", 20m));
        await mediator.SendAsync<CreatePayment, string>(new CreatePayment("PAY-C", 30m));

        faults.Should().HaveCount(3);
        faults.Select(f => f.Message.PaymentId).Should().Equal("PAY-A", "PAY-B", "PAY-C");
    }

    [Fact]
    public async Task Fault_NoSubscriber_DoesNotThrow()
    {
        // No FaultCapture registered — fault publishing should be fire-and-forget
        await using var sp = BuildSp(
            _ => CatgaResult<string>.Failure("error"),
            faultCapture: null);

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var act = async () => await mediator.SendAsync<CreatePayment, string>(
            new CreatePayment("PAY-X", 1m));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Fault_ImplementsIEvent()
    {
        var fault = new Fault<CreatePayment>(new CreatePayment("P", 1m));
        fault.Should().BeAssignableTo<IEvent>();
    }

    [Fact]
    public void Fault_WithException_CapturesExceptionDetails()
    {
        var ex = new ArgumentException("invalid amount");
        var fault = new Fault<CreatePayment>(new CreatePayment("P", -1m), ex);

        fault.Exception.Should().BeSameAs(ex);
        fault.ErrorMessage.Should().Be("invalid amount");
        fault.ErrorCode.Should().Be("ArgumentException");
    }

    [Fact]
    public void Fault_WithExplicitErrorCode_UsesProvidedCode()
    {
        var fault = new Fault<CreatePayment>(
            new CreatePayment("P", 1m),
            errorCode: ErrorCodes.ValidationFailed,
            errorMessage: "Amount must be positive");

        fault.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        fault.ErrorMessage.Should().Be("Amount must be positive");
    }

    [Fact]
    public void Fault_InheritsCorrelationIdFromMessage()
    {
        var msg = new CreatePayment("P", 1m) { MessageId = 100L, CorrelationId = 50L };
        var fault = new Fault<CreatePayment>(msg);
        fault.CorrelationId.Should().Be(50L);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// E2E: CORRELATIONID PROPAGATION SCENARIOS
// ═══════════════════════════════════════════════════════════════════════════════

public class CorrelationIdE2ETests
{
    private static void ClearCaches()
    {
        var t = typeof(CatgaMediator);
        (t.GetField("_handlerCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_behaviorCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
    }

    private static ServiceProvider BuildSp(Action<IServiceCollection>? configure = null)
    {
        ClearCaches();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CorrelationId_SetFromMessageId_WhenNoCorrelationId()
    {
        long? captured = null;
        await using var sp = BuildSp(s =>
            s.AddSingleton<IRequestHandler<CreatePayment, string>>(sp2 =>
                new LambdaHandler<CreatePayment, string>(req =>
                {
                    captured = sp2.GetRequiredService<ICorrelationContext>().Current;
                    return CatgaResult<string>.Success("ok");
                })));

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        await mediator.SendAsync<CreatePayment, string>(
            new CreatePayment("P", 1m) { MessageId = 777L });

        captured.Should().Be(777L);
    }

    [Fact]
    public async Task CorrelationId_UsesExistingCorrelationId_WhenSet()
    {
        long? captured = null;
        await using var sp = BuildSp(s =>
            s.AddSingleton<IRequestHandler<CreatePayment, string>>(sp2 =>
                new LambdaHandler<CreatePayment, string>(req =>
                {
                    captured = sp2.GetRequiredService<ICorrelationContext>().Current;
                    return CatgaResult<string>.Success("ok");
                })));

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        await mediator.SendAsync<CreatePayment, string>(
            new CreatePayment("P", 1m) { MessageId = 100L, CorrelationId = 42L });

        captured.Should().Be(42L); // uses CorrelationId, not MessageId
    }

    [Fact]
    public async Task CorrelationId_ClearedAfterHandler()
    {
        var ctx = new CorrelationContext();
        ClearCaches();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ICorrelationContext>(ctx);
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>));
        services.AddSingleton<IRequestHandler<CreatePayment, string>>(
            _ => new LambdaHandler<CreatePayment, string>(_ => CatgaResult<string>.Success("ok")));

        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        await mediator.SendAsync<CreatePayment, string>(
            new CreatePayment("P", 1m) { MessageId = 99L });

        ctx.Current.Should().BeNull(); // cleared after handler completes
    }

    [Fact]
    public async Task CorrelationId_PropagatedToPublishedEvents()
    {
        long? eventCorrelationId = null;
        await using var sp = BuildSp(s =>
        {
            s.AddSingleton<IRequestHandler<CreatePayment, string>>(sp2 =>
                new LambdaHandler<CreatePayment, string>(async req =>
                {
                    await sp2.GetRequiredService<ICatgaMediator>()
                        .PublishAsync(new PaymentCreated(req.PaymentId));
                    return CatgaResult<string>.Success("ok");
                }));
            s.AddSingleton<IEventHandler<PaymentCreated>>(sp2 =>
                new LambdaEventHandler<PaymentCreated>(e =>
                {
                    eventCorrelationId = sp2.GetRequiredService<ICorrelationContext>().Current;
                }));
        });

        var mediator = sp.GetRequiredService<ICatgaMediator>();
        await mediator.SendAsync<CreatePayment, string>(
            new CreatePayment("P", 1m) { MessageId = 55L });

        eventCorrelationId.Should().Be(55L);
    }

    [Fact]
    public async Task CorrelationId_ConcurrentRequests_Isolated()
    {
        var results = new System.Collections.Concurrent.ConcurrentDictionary<long, long?>();

        await using var sp = BuildSp(s =>
            s.AddSingleton<IRequestHandler<CreatePayment, string>>(sp2 =>
                new LambdaHandler<CreatePayment, string>(async req =>
                {
                    await Task.Delay(Random.Shared.Next(1, 10));
                    var corr = sp2.GetRequiredService<ICorrelationContext>().Current;
                    results[req.MessageId] = corr;
                    return CatgaResult<string>.Success("ok");
                })));

        var mediator = sp.GetRequiredService<ICatgaMediator>();

        await Task.WhenAll(Enumerable.Range(1, 10).Select(i =>
            mediator.SendAsync<CreatePayment, string>(
                new CreatePayment($"P{i}", i) { MessageId = i }).AsTask()));

        // Each request should see its own MessageId as correlation
        for (int i = 1; i <= 10; i++)
            results[i].Should().Be(i, $"request {i} should have correlationId {i}");
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file sealed class LambdaHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly Func<TRequest, Task<CatgaResult<TResponse>>> _handler;

    public LambdaHandler(Func<TRequest, CatgaResult<TResponse>> handler)
        => _handler = r => Task.FromResult(handler(r));

    public LambdaHandler(Func<TRequest, Task<CatgaResult<TResponse>>> handler)
        => _handler = handler;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken ct = default)
        => await _handler(request);
}

file sealed class LambdaEventHandler<TEvent> : IEventHandler<TEvent> where TEvent : IEvent
{
    private readonly Action<TEvent> _handler;
    public LambdaEventHandler(Action<TEvent> handler) => _handler = handler;
    public ValueTask HandleAsync(TEvent @event, CancellationToken ct = default)
    {
        _handler(@event);
        return ValueTask.CompletedTask;
    }
}

file sealed class FaultCapture<T> : IEventHandler<Fault<T>> where T : IMessage
{
    private readonly List<Fault<T>> _faults;
    public FaultCapture(List<Fault<T>> faults) => _faults = faults;
    public ValueTask HandleAsync(Fault<T> @event, CancellationToken ct = default)
    {
        _faults.Add(@event);
        return ValueTask.CompletedTask;
    }
}
