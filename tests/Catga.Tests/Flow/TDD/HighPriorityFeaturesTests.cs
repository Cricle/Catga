using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow.Dsl;
using Catga.Flow.DependencyInjection;
using Catga.Flow.StateMachine;
using Catga.Messaging;
using Catga.Outbox;
using Catga.Persistence.InMemory.Flow;
using Catga.Resilience;
using Catga.Testing;
using Catga.Transport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.TDD;

// ═══════════════════════════════════════════════════════════════════════════════
// SHARED DOMAIN TYPES
// ═══════════════════════════════════════════════════════════════════════════════

public enum PaymentStatus { Pending, Authorized, Captured, Refunded, Failed }

public class PaymentState : IStateMachineState<PaymentStatus>
{
    public string? FlowId { get; set; }
    public PaymentStatus CurrentState { get; set; } = PaymentStatus.Pending;
    public string? AuthCode { get; set; }
    public decimal Amount { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record PaymentAuthorized(string AuthCode, decimal Amount) : IEvent
{
    public long MessageId { get; init; }
}
public record PaymentCaptured(string AuthCode) : IEvent { public long MessageId { get; init; } }
public record PaymentFailed(string Reason) : IEvent { public long MessageId { get; init; } }
public record PaymentRefunded : IEvent { public long MessageId { get; init; } }

public class PaymentStateMachine : StateMachineConfig<PaymentState, PaymentStatus>
{
    protected override void Configure()
    {
        State(PaymentStatus.Pending)
            .On<PaymentAuthorized>()
                .Execute((s, e) => { s.AuthCode = e.AuthCode; s.Amount = e.Amount; })
                .TransitionTo(PaymentStatus.Authorized)
            .And()
            .On<PaymentFailed>()
                .TransitionTo(PaymentStatus.Failed);

        State(PaymentStatus.Authorized)
            .On<PaymentCaptured>()
                .TransitionTo(PaymentStatus.Captured)
            .And()
            .On<PaymentRefunded>()
                .TransitionTo(PaymentStatus.Refunded);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 1. STATE MACHINE AUTO EVENT ROUTING TESTS (RED)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for IStateMachineEventRouter — automatically routes published events
/// to the correct state machine instance without manual HandleAsync calls.
/// MassTransit equivalent: Saga with InitiatedBy/Orchestrates.
/// </summary>
public class StateMachineEventRouterTests
{
    [Fact]
    public void IStateMachineEventRouter_InterfaceExists()
    {
        // RED: IStateMachineEventRouter does not exist yet
        typeof(IStateMachineEventRouter<,>).Should().NotBeNull();
    }

    [Fact]
    public async Task EventRouter_WhenEventPublished_RoutesToCorrectInstance()
    {
        var store = new InMemoryDslFlowStore();
        var executor = new StateMachineExecutor<PaymentState, PaymentStatus, PaymentStateMachine>(store);
        await executor.InitializeAsync("pay-001", PaymentStatus.Pending);

        var router = new StateMachineEventRouter<PaymentState, PaymentStatus, PaymentStateMachine>(executor)
            .For<PaymentAuthorized>(e => "pay-001");

        await router.RouteAsync(new PaymentAuthorized("AUTH-1", 99.99m));

        var state = await executor.GetStateAsync("pay-001");
        state!.CurrentState.Should().Be(PaymentStatus.Authorized);
        state.AuthCode.Should().Be("AUTH-1");
    }

    [Fact]
    public async Task EventRouter_WhenInstanceNotFound_CreatesNewInstance()
    {
        var store = new InMemoryDslFlowStore();
        var executor = new StateMachineExecutor<PaymentState, PaymentStatus, PaymentStateMachine>(store);

        var router = new StateMachineEventRouter<PaymentState, PaymentStatus, PaymentStateMachine>(executor)
            .For<PaymentAuthorized>(e => "pay-new");

        await router.RouteAsync(new PaymentAuthorized("AUTH-NEW", 50m));

        var state = await executor.GetStateAsync("pay-new");
        state.Should().NotBeNull();
        state!.CurrentState.Should().Be(PaymentStatus.Authorized);
    }

    [Fact]
    public async Task EventRouter_MultipleEvents_CorrectStateTransitions()
    {
        var store = new InMemoryDslFlowStore();
        var executor = new StateMachineExecutor<PaymentState, PaymentStatus, PaymentStateMachine>(store);

        var router = new StateMachineEventRouter<PaymentState, PaymentStatus, PaymentStateMachine>(executor)
            .ForAll(e => "pay-multi");

        await router.RouteAsync(new PaymentAuthorized("AUTH-M", 200m));
        await router.RouteAsync(new PaymentCaptured("AUTH-M"));

        var state = await executor.GetStateAsync("pay-multi");
        state!.CurrentState.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public void EventRouter_RegisteredInDI_CanBeResolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachineWithRouter<PaymentState, PaymentStatus, PaymentStateMachine>(
            configure: r => r.For<PaymentAuthorized>(e => e.AuthCode));

        var sp = services.BuildServiceProvider();
        sp.GetService<IStateMachineEventRouter<PaymentState, PaymentStatus>>().Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 2. OUTBOX + COMPETING CONSUMERS INTEGRATION TESTS (RED)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for OutboxCompetingProcessor — Outbox messages processed by a
/// competing consumer so only one node processes each message in multi-node setup.
/// </summary>
public class OutboxCompetingProcessorTests
{
    [Fact]
    public void IOutboxCompetingProcessor_InterfaceExists()
    {
        // RED: IOutboxCompetingProcessor does not exist yet
        typeof(IOutboxCompetingProcessor).Should().NotBeNull();
    }

    [Fact]
    public async Task OutboxCompetingProcessor_ProcessesPendingMessages()
    {
        // RED: OutboxCompetingProcessor does not exist yet
        var outboxStore = new MemoryOutboxStore(new DefaultResiliencePipelineProvider(new CatgaResilienceOptions()));

        var published = new System.Collections.Concurrent.ConcurrentBag<string>();
        var transport = new FakeTransport(msg => published.Add(msg));

        var processor = new OutboxCompetingProcessor(outboxStore, transport);

        // Add a message to outbox
        await outboxStore.AddAsync(new OutboxMessage
        {
            MessageId = 1,
            MessageType = "TestMessage",
            Payload = System.Text.Encoding.UTF8.GetBytes("hello"),
            Status = OutboxStatus.Pending
        });

        await processor.ProcessBatchAsync(CancellationToken.None);

        published.Should().HaveCount(1);
    }

    [Fact]
    public async Task OutboxCompetingProcessor_WithMultipleProcessors_EachMessageProcessedOnce()
    {
        var outboxStore = new MemoryOutboxStore(new DefaultResiliencePipelineProvider(new CatgaResilienceOptions()));

        var processedCount = 0;
        var transport = new FakeTransport(_ => Interlocked.Increment(ref processedCount));

        // Add 10 messages
        for (int i = 1; i <= 10; i++)
            await outboxStore.AddAsync(new OutboxMessage
            {
                MessageId = i, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
            });

        // Two processors competing
        var p1 = new OutboxCompetingProcessor(outboxStore, transport);
        var p2 = new OutboxCompetingProcessor(outboxStore, transport);

        await Task.WhenAll(
            p1.ProcessBatchAsync(CancellationToken.None),
            p2.ProcessBatchAsync(CancellationToken.None));

        processedCount.Should().Be(10); // each message processed exactly once
    }

    private sealed class FakeTransport : IMessageTransport
    {
        private readonly Action<string> _onPublish;
        public FakeTransport(Action<string> onPublish) => _onPublish = onPublish;
        public string Name => "Fake";
        public BatchTransportOptions? BatchOptions => null;
        public CompressionTransportOptions? CompressionOptions => null;
        public Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken ct = default) where TMessage : class { _onPublish(typeof(TMessage).Name); return Task.CompletedTask; }
        public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 3. MESSAGE ROUTING ENHANCEMENT TESTS (RED)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for Header-based routing and priority queue support.
/// </summary>
public class MessageRoutingTests
{
    [Fact]
    public void MessageRouter_InterfaceExists()
    {
        // RED: IMessageRouter does not exist yet
        typeof(IMessageRouter).Should().NotBeNull();
    }

    [Fact]
    public void MessageRouter_RouteByHeader_SelectsCorrectDestination()
    {
        var router = new MessageRouter();
        router.AddRoute("region", "eu", "queue.eu");
        router.AddRoute("region", "us", "queue.us");

        var ctx = new TransportContext { Metadata = new Dictionary<string, string> { ["region"] = "eu" } };
        var dest = router.Resolve(ctx);

        dest.Should().Be("queue.eu");
    }

    [Fact]
    public void MessageRouter_NoMatchingHeader_ReturnsDefault()
    {
        var router = new MessageRouter(defaultDestination: "queue.default");
        router.AddRoute("region", "eu", "queue.eu");

        var ctx = new TransportContext { Metadata = new Dictionary<string, string> { ["region"] = "au" } };
        router.Resolve(ctx).Should().Be("queue.default");
    }

    [Fact]
    public void PriorityTransportContext_HighPriority_HasCorrectValue()
    {
        // RED: PriorityTransportContext does not exist yet
        var ctx = new PriorityTransportContext(MessagePriority.High);
        ctx.Priority.Should().Be(MessagePriority.High);
        ctx.Context.Metadata.Should().ContainKey("x-priority");
    }

    [Fact]
    public void PriorityTransportContext_Critical_HasHighestPriority()
    {
        var ctx = new PriorityTransportContext(MessagePriority.Critical);
        ctx.Context.Metadata!["x-priority"].Should().Be("3");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 4. FLOW REMOTE SEND TESTS (RED)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for RemoteSend step in Flow DSL — calls a remote service via IRequestClient
/// and maps the response into flow state.
/// </summary>
public class FlowRemoteSendTests
{
    public record GetInventory(string ProductId) : IRequest<InventoryResult>
    {
        public long MessageId { get; init; }
    }
    public record InventoryResult(int Available);

    public class RemoteFlowState : IFlowState
    {
        public string? FlowId { get; set; }
        public string ProductId { get; set; } = "";
        public int AvailableStock { get; set; }
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int i) => false;
        public void ClearChanges() { }
        public void MarkChanged(int i) { }
        public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
    }

    [Fact]
    public void FlowBuilder_HasRemoteSend_Extension()
    {
        // RED: RemoteSend extension does not exist yet
        var builder = new InlineFlowConfig<RemoteFlowState>(flow =>
        {
            flow.RemoteSend<RemoteFlowState, GetInventory, InventoryResult>(
                    s => new GetInventory(s.ProductId))
                .Into((s, r) => s.AvailableStock = r.Available);
        });
        builder.Build();
        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.RemoteSend);
    }

    [Fact]
    public async Task FlowRemoteSend_CallsRequestClient_AndMapsResult()
    {
        var requestClientFactory = new TestRequestClientFactory()
            .OnRequest<GetInventory, InventoryResult>((request, _, _) =>
                CatgaResult<InventoryResult>.Success(new InventoryResult(42)));
        await using var ctx = new FlowTestContext<RemoteFlowState, RemoteFlowConfig>(
            services => services.AddSingleton<IRequestClientFactory>(requestClientFactory));

        var result = await ctx.RunAsync(new RemoteFlowState { ProductId = "PROD-1" });

        result.IsSuccess.Should().BeTrue();
        result.State!.AvailableStock.Should().Be(42);
        ctx.Mediator.Sent.Should().BeEmpty();
        requestClientFactory.Requests.Should().ContainSingle()
            .Which.Should().BeOfType<GetInventory>()
            .Which.ProductId.Should().Be("PROD-1");
    }

    private sealed class FakeInventoryHandler : IRequestHandler<GetInventory, InventoryResult>
    {
        public ValueTask<CatgaResult<InventoryResult>> HandleAsync(GetInventory request, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<InventoryResult>.Success(new InventoryResult(42)));
    }

    private sealed class RemoteFlowConfig : FlowConfig<RemoteFlowState>
    {
        protected override void Configure(IFlowBuilder<RemoteFlowState> flow)
        {
            flow.RemoteSend<RemoteFlowState, GetInventory, InventoryResult>(
                    s => new GetInventory(s.ProductId))
                .Into((s, r) => s.AvailableStock = r.Available);
        }
    }
}

// Helper used in tests
internal sealed class InlineFlowConfig<TState> : FlowConfig<TState>
    where TState : class, IFlowState, new()
{
    private readonly Action<IFlowBuilder<TState>> _configure;
    public InlineFlowConfig(Action<IFlowBuilder<TState>> configure) => _configure = configure;
    protected override void Configure(IFlowBuilder<TState> flow) => _configure(flow);
}

internal sealed class TestRequestClientFactory : IRequestClientFactory
{
    private readonly Dictionary<(Type RequestType, Type ResponseType), object> _handlers = new();

    public List<object> Requests { get; } = [];
    public List<(Type RequestType, Type ResponseType, string? Destination, TimeSpan? Timeout)> CreatedClients { get; } = [];

    public TestRequestClientFactory OnRequest<TRequest, TResponse>(
        Func<TRequest, TimeSpan, CancellationToken, CatgaResult<TResponse>> handler)
        where TRequest : class, IRequest<TResponse>
        where TResponse : class
    {
        _handlers[(typeof(TRequest), typeof(TResponse))] = handler;
        return this;
    }

    public IRequestClient<TRequest, TResponse> CreateClient<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse>(
        string? destination = null,
        TimeSpan? defaultTimeout = null)
        where TRequest : class, IRequest<TResponse>
        where TResponse : class
    {
        CreatedClients.Add((typeof(TRequest), typeof(TResponse), destination, defaultTimeout));

        if (!_handlers.TryGetValue((typeof(TRequest), typeof(TResponse)), out var rawHandler))
            throw new InvalidOperationException($"No request client configured for {typeof(TRequest).Name} -> {typeof(TResponse).Name}");

        var handler = (Func<TRequest, TimeSpan, CancellationToken, CatgaResult<TResponse>>)rawHandler;
        return new TestRequestClient<TRequest, TResponse>(request =>
        {
            Requests.Add(request);
            return Task.FromResult(handler(request, defaultTimeout ?? TimeSpan.FromSeconds(30), CancellationToken.None));
        });
    }

    private sealed class TestRequestClient<TRequest, TResponse> : IRequestClient<TRequest, TResponse>
        where TRequest : class, IRequest<TResponse>
        where TResponse : class
    {
        private readonly Func<TRequest, Task<CatgaResult<TResponse>>> _handler;

        public TestRequestClient(Func<TRequest, Task<CatgaResult<TResponse>>> handler) => _handler = handler;

        public Task<CatgaResult<TResponse>> RequestAsync(TRequest request, CancellationToken ct = default)
            => _handler(request);

        public Task<CatgaResult<TResponse>> RequestAsync(TRequest request, TimeSpan timeout, CancellationToken ct = default)
            => _handler(request);
    }
}
