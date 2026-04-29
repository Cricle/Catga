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
// E2E SCENARIO 1: Full Order Saga with State Machine Auto-Routing
// ═══════════════════════════════════════════════════════════════════════════════

public enum OrderSagaStatus { Created, PaymentPending, Paid, Fulfilling, Shipped, Completed, Cancelled }

public class OrderSagaState : IStateMachineState<OrderSagaStatus>
{
    public string? FlowId { get; set; }
    public OrderSagaStatus CurrentState { get; set; } = OrderSagaStatus.Created;
    public string? OrderId { get; set; }
    public decimal Total { get; set; }
    public string? PaymentRef { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CancelReason { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record OrderCreatedEvt(string OrderId, decimal Total) : IEvent { public long MessageId { get; init; } }
public record PaymentReceivedEvt(string OrderId, string PaymentRef) : IEvent { public long MessageId { get; init; } }
public record FulfillmentStartedEvt(string OrderId) : IEvent { public long MessageId { get; init; } }
public record OrderShippedEvt(string OrderId, string TrackingNumber) : IEvent { public long MessageId { get; init; } }
public record OrderCancelledEvt(string OrderId, string Reason) : IEvent { public long MessageId { get; init; } }

public class OrderSagaMachine : StateMachineConfig<OrderSagaState, OrderSagaStatus>
{
    protected override void Configure()
    {
        State(OrderSagaStatus.Created)
            .On<PaymentReceivedEvt>()
                .Execute((s, e) => { s.PaymentRef = e.PaymentRef; })
                .TransitionTo(OrderSagaStatus.Paid)
            .And()
            .On<OrderCancelledEvt>()
                .Execute((s, e) => s.CancelReason = e.Reason)
                .TransitionTo(OrderSagaStatus.Cancelled);

        State(OrderSagaStatus.Paid)
            .On<FulfillmentStartedEvt>()
                .TransitionTo(OrderSagaStatus.Fulfilling)
            .And()
            .On<OrderCancelledEvt>()
                .Execute((s, e) => s.CancelReason = e.Reason)
                .TransitionTo(OrderSagaStatus.Cancelled);

        State(OrderSagaStatus.Fulfilling)
            .On<OrderShippedEvt>()
                .Execute((s, e) => s.TrackingNumber = e.TrackingNumber)
                .TransitionTo(OrderSagaStatus.Shipped);

        State(OrderSagaStatus.Shipped)
            .On<OrderShippedEvt>()
                .TransitionTo(OrderSagaStatus.Completed);
    }
}

public class StateMachineAutoRoutingE2ETests
{
    private static StateMachineExecutor<OrderSagaState, OrderSagaStatus, OrderSagaMachine> CreateExecutor()
        => new(new InMemoryDslFlowStore());

    [Fact]
    public async Task FullOrderSaga_HappyPath_AllTransitionsCorrect()
    {
        var executor = CreateExecutor();
        var router = new StateMachineEventRouter<OrderSagaState, OrderSagaStatus, OrderSagaMachine>(executor)
            .For<PaymentReceivedEvt>(e => e.OrderId)
            .For<FulfillmentStartedEvt>(e => e.OrderId)
            .For<OrderShippedEvt>(e => e.OrderId)
            .For<OrderCancelledEvt>(e => e.OrderId);

        await executor.InitializeAsync("ORD-001", OrderSagaStatus.Created,
            s => { s.OrderId = "ORD-001"; s.Total = 150m; });

        await router.RouteAsync(new PaymentReceivedEvt("ORD-001", "PAY-XYZ"));
        await router.RouteAsync(new FulfillmentStartedEvt("ORD-001"));
        await router.RouteAsync(new OrderShippedEvt("ORD-001", "TRK-123"));

        var state = await executor.GetStateAsync("ORD-001");
        state!.CurrentState.Should().Be(OrderSagaStatus.Shipped);
        state.PaymentRef.Should().Be("PAY-XYZ");
        state.TrackingNumber.Should().Be("TRK-123");
    }

    [Fact]
    public async Task FullOrderSaga_CancelAfterPayment_TransitionsToCancelled()
    {
        var executor = CreateExecutor();
        var router = new StateMachineEventRouter<OrderSagaState, OrderSagaStatus, OrderSagaMachine>(executor)
            .For<PaymentReceivedEvt>(e => e.OrderId)
            .For<OrderCancelledEvt>(e => e.OrderId);

        await executor.InitializeAsync("ORD-002", OrderSagaStatus.Created);
        await router.RouteAsync(new PaymentReceivedEvt("ORD-002", "PAY-ABC"));
        await router.RouteAsync(new OrderCancelledEvt("ORD-002", "Customer request"));

        var state = await executor.GetStateAsync("ORD-002");
        state!.CurrentState.Should().Be(OrderSagaStatus.Cancelled);
        state.CancelReason.Should().Be("Customer request");
    }

    [Fact]
    public async Task EventRouter_UnknownEvent_DoesNothing()
    {
        var executor = CreateExecutor();
        var router = new StateMachineEventRouter<OrderSagaState, OrderSagaStatus, OrderSagaMachine>(executor)
            .For<PaymentReceivedEvt>(e => e.OrderId);

        await executor.InitializeAsync("ORD-003", OrderSagaStatus.Created);
        // Route an event with no resolver — should be silently ignored
        await router.RouteAsync(new FulfillmentStartedEvt("ORD-003"));

        var state = await executor.GetStateAsync("ORD-003");
        state!.CurrentState.Should().Be(OrderSagaStatus.Created); // unchanged
    }

    [Fact]
    public async Task EventRouter_MultipleInstances_RoutedIndependently()
    {
        var executor = CreateExecutor();
        var router = new StateMachineEventRouter<OrderSagaState, OrderSagaStatus, OrderSagaMachine>(executor)
            .For<PaymentReceivedEvt>(e => e.OrderId);

        await executor.InitializeAsync("ORD-A", OrderSagaStatus.Created);
        await executor.InitializeAsync("ORD-B", OrderSagaStatus.Created);

        await router.RouteAsync(new PaymentReceivedEvt("ORD-A", "PAY-A"));
        // ORD-B not paid yet

        var stateA = await executor.GetStateAsync("ORD-A");
        var stateB = await executor.GetStateAsync("ORD-B");

        stateA!.CurrentState.Should().Be(OrderSagaStatus.Paid);
        stateB!.CurrentState.Should().Be(OrderSagaStatus.Created);
    }

    [Fact]
    public void AddStateMachineWithRouter_DI_ResolvesRouter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachineWithRouter<OrderSagaState, OrderSagaStatus, OrderSagaMachine>(
            r => r.For<PaymentReceivedEvt>(e => e.OrderId)
                  .For<OrderCancelledEvt>(e => e.OrderId));

        var sp = services.BuildServiceProvider();
        var router = sp.GetRequiredService<IStateMachineEventRouter<OrderSagaState, OrderSagaStatus>>();
        router.Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// E2E SCENARIO 2: Outbox + Competing Consumers
// ═══════════════════════════════════════════════════════════════════════════════

public class OutboxCompetingProcessorE2ETests
{
    private static MemoryOutboxStore CreateOutbox()
        => new(new DefaultResiliencePipelineProvider(new CatgaResilienceOptions()));

    [Fact]
    public async Task OutboxCC_SingleProcessor_PublishesAllMessages()
    {
        var outbox = CreateOutbox();
        var published = new System.Collections.Concurrent.ConcurrentBag<long>();
        var transport = new CountingTransport(id => published.Add(id));

        for (int i = 1; i <= 5; i++)
            await outbox.AddAsync(new OutboxMessage
            {
                MessageId = i, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
            });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        published.Should().HaveCount(5);
    }

    [Fact]
    public async Task OutboxCC_TwoProcessors_EachMessagePublishedOnce()
    {
        var outbox = CreateOutbox();
        var publishCount = 0;
        var transport = new CountingTransport(_ => Interlocked.Increment(ref publishCount));

        for (int i = 1; i <= 20; i++)
            await outbox.AddAsync(new OutboxMessage
            {
                MessageId = i, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
            });

        var p1 = new OutboxCompetingProcessor(outbox, transport);
        var p2 = new OutboxCompetingProcessor(outbox, transport);

        await Task.WhenAll(p1.ProcessBatchAsync(), p2.ProcessBatchAsync());

        publishCount.Should().Be(20);
    }

    [Fact]
    public async Task OutboxCC_ScheduledMessage_NotPublishedBeforeDue()
    {
        var outbox = CreateOutbox();
        var published = new System.Collections.Concurrent.ConcurrentBag<long>();
        var transport = new CountingTransport(id => published.Add(id));

        // Future message — not due yet
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = 99, MessageType = "T", Payload = [],
            Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        // Immediate message
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = 100, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
        });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        published.Should().HaveCount(1);
        published.Should().Contain(100);
        published.Should().NotContain(99);
    }

    [Fact]
    public async Task OutboxCC_FailedTransport_MarksMessageAsFailed()
    {
        var outbox = CreateOutbox();
        var transport = new FailingTransport();

        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = 1, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
        });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        var pending = await outbox.GetPendingMessagesAsync(10);
        // Message stays pending for retry (with incremented RetryCount)
        pending.Should().HaveCount(1);
        pending[0].RetryCount.Should().BeGreaterThan(0);
    }

    private sealed class CountingTransport : IMessageTransport
    {
        private readonly Action<long> _onPublish;
        public CountingTransport(Action<long> onPublish) => _onPublish = onPublish;
        public string Name => "Counting";
        public BatchTransportOptions? BatchOptions => null;
        public CompressionTransportOptions? CompressionOptions => null;
        public Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken ct = default) where TMessage : class
        {
            if (message is OutboxCompetingProcessor.EnvelopeMessage env)
                _onPublish(env.Inner.MessageId);
            else
                _onPublish(0);
            return Task.CompletedTask;
        }
        public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
    }

    private sealed class FailingTransport : IMessageTransport
    {
        public string Name => "Failing";
        public BatchTransportOptions? BatchOptions => null;
        public CompressionTransportOptions? CompressionOptions => null;
        public Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken ct = default) where TMessage : class
            => throw new InvalidOperationException("Transport failure");
        public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// E2E SCENARIO 3: Message Routing
// ═══════════════════════════════════════════════════════════════════════════════

public class MessageRoutingE2ETests
{
    [Fact]
    public void Router_EURegion_RoutesToEUQueue()
    {
        var router = new MessageRouter("queue.default");
        router.AddRoute("region", "eu", "queue.eu");
        router.AddRoute("region", "us", "queue.us");
        router.AddRoute("region", "ap", "queue.ap");

        router.Resolve(new TransportContext { Metadata = new Dictionary<string, string> { ["region"] = "eu" } })
            .Should().Be("queue.eu");
        router.Resolve(new TransportContext { Metadata = new Dictionary<string, string> { ["region"] = "us" } })
            .Should().Be("queue.us");
        router.Resolve(new TransportContext { Metadata = new Dictionary<string, string> { ["region"] = "ap" } })
            .Should().Be("queue.ap");
    }

    [Fact]
    public void Router_UnknownRegion_FallsBackToDefault()
    {
        var router = new MessageRouter("queue.default");
        router.AddRoute("region", "eu", "queue.eu");

        router.Resolve(new TransportContext { Metadata = new Dictionary<string, string> { ["region"] = "za" } })
            .Should().Be("queue.default");
    }

    [Fact]
    public void Router_NoMetadata_ReturnsDefault()
    {
        var router = new MessageRouter("queue.default");
        router.Resolve(new TransportContext()).Should().Be("queue.default");
    }

    [Fact]
    public void Router_MultipleHeaderRules_FirstMatchWins()
    {
        var router = new MessageRouter();
        router.AddRoute("priority", "high", "queue.priority");
        router.AddRoute("region", "eu", "queue.eu");

        var ctx = new TransportContext
        {
            Metadata = new Dictionary<string, string> { ["priority"] = "high", ["region"] = "eu" }
        };
        router.Resolve(ctx).Should().Be("queue.priority");
    }

    [Fact]
    public void PriorityContext_AllLevels_CorrectMetadata()
    {
        new PriorityTransportContext(MessagePriority.Low).Context.Metadata!["x-priority"].Should().Be("0");
        new PriorityTransportContext(MessagePriority.Normal).Context.Metadata!["x-priority"].Should().Be("1");
        new PriorityTransportContext(MessagePriority.High).Context.Metadata!["x-priority"].Should().Be("2");
        new PriorityTransportContext(MessagePriority.Critical).Context.Metadata!["x-priority"].Should().Be("3");
    }

    [Fact]
    public void PriorityContext_StaticFactories_CorrectPriority()
    {
        PriorityTransportContext.High.Priority.Should().Be(MessagePriority.High);
        PriorityTransportContext.Critical.Priority.Should().Be(MessagePriority.Critical);
        PriorityTransportContext.Normal.Priority.Should().Be(MessagePriority.Normal);
    }

    [Fact]
    public void Router_CanBeUsedWithPriorityContext()
    {
        var router = new MessageRouter("queue.normal");
        router.AddRoute("x-priority", "3", "queue.critical");
        router.AddRoute("x-priority", "2", "queue.high");

        var criticalCtx = PriorityTransportContext.Critical.Context;
        router.Resolve(criticalCtx).Should().Be("queue.critical");

        var highCtx = PriorityTransportContext.High.Context;
        router.Resolve(highCtx).Should().Be("queue.high");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// E2E SCENARIO 4: Flow RemoteSend
// ═══════════════════════════════════════════════════════════════════════════════

public class FlowRemoteSendE2ETests
{
    public record CheckStock(string Sku) : IRequest<StockResult> { public long MessageId { get; init; } }
    public record StockResult(int Qty, bool Available);
    public record ReserveItem(string Sku, int Qty) : IRequest<string> { public long MessageId { get; init; } }

    public class CheckoutState : IFlowState
    {
        public string? FlowId { get; set; }
        public string Sku { get; set; } = "";
        public int Qty { get; set; }
        public bool InStock { get; set; }
        public string? ReservationId { get; set; }
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int i) => false;
        public void ClearChanges() { }
        public void MarkChanged(int i) { }
        public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
    }

    public class CheckoutFlow : FlowConfig<CheckoutState>
    {
        protected override void Configure(IFlowBuilder<CheckoutState> flow)
        {
            // Step 1: RemoteSend to check stock
            flow.RemoteSend<CheckoutState, CheckStock, StockResult>(s => new CheckStock(s.Sku))
                .Into((s, r) => { s.InStock = r.Available; s.Qty = r.Qty; });

            // Step 2: Reserve only if in stock (optional step skipped when out of stock)
            flow.RemoteSend<CheckoutState, ReserveItem, string>(s => new ReserveItem(s.Sku, s.Qty))
                .Into((s, r) => s.ReservationId = r)
                .OnlyWhen(s => s.InStock);
        }
    }

    [Fact]
    public async Task RemoteSend_InStock_ReservesItem()
    {
        var requestClientFactory = new TestRequestClientFactory()
            .OnRequest<CheckStock, StockResult>((_, _, _) =>
                CatgaResult<StockResult>.Success(new StockResult(10, true)))
            .OnRequest<ReserveItem, string>((_, _, _) =>
                CatgaResult<string>.Success("RES-001"));
        await using var ctx = new FlowTestContext<CheckoutState, CheckoutFlow>(
            services => services.AddSingleton<IRequestClientFactory>(requestClientFactory));

        var result = await ctx.RunAsync(new CheckoutState { Sku = "SKU-A" });

        result.IsSuccess.Should().BeTrue();
        result.State!.InStock.Should().BeTrue();
        result.State.ReservationId.Should().Be("RES-001");
        ctx.Mediator.Sent.Should().BeEmpty();
        requestClientFactory.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoteSend_OutOfStock_SkipsReservation()
    {
        var requestClientFactory = new TestRequestClientFactory()
            .OnRequest<CheckStock, StockResult>((_, _, _) =>
                CatgaResult<StockResult>.Success(new StockResult(0, false)));
        await using var ctx = new FlowTestContext<CheckoutState, CheckoutFlow>(
            services => services.AddSingleton<IRequestClientFactory>(requestClientFactory));

        var result = await ctx.RunAsync(new CheckoutState { Sku = "SKU-B" });

        result.IsSuccess.Should().BeTrue();
        result.State!.InStock.Should().BeFalse();
        result.State.ReservationId.Should().BeNull();
        requestClientFactory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RemoteSend_ServiceFails_FlowFails()
    {
        var requestClientFactory = new TestRequestClientFactory()
            .OnRequest<CheckStock, StockResult>((_, _, _) =>
                CatgaResult<StockResult>.Failure("inventory service unavailable"));
        await using var ctx = new FlowTestContext<CheckoutState, CheckoutFlow>(
            services => services.AddSingleton<IRequestClientFactory>(requestClientFactory));

        var result = await ctx.RunAsync(new CheckoutState { Sku = "SKU-C" });

        result.IsSuccess.Should().BeFalse();
        ctx.Mediator.Sent.Should().BeEmpty();
    }

    [Fact]
    public void RemoteSend_StepType_IsRemoteSend()
    {
        var config = new CheckoutFlow();
        config.Build();
        config.Steps[0].Type.Should().Be(StepType.RemoteSend);
    }

    [Fact]
    public async Task RemoteSend_MultipleRemoteCalls_AllExecuted()
    {
        var requestClientFactory = new TestRequestClientFactory()
            .OnRequest<CheckStock, StockResult>((_, _, _) =>
                CatgaResult<StockResult>.Success(new StockResult(5, true)))
            .OnRequest<ReserveItem, string>((_, _, _) =>
                CatgaResult<string>.Success("RES-MULTI"));
        await using var ctx = new FlowTestContext<CheckoutState, CheckoutFlow>(
            services => services.AddSingleton<IRequestClientFactory>(requestClientFactory));

        await ctx.RunAsync(new CheckoutState { Sku = "SKU-D" });

        requestClientFactory.Requests.Should().HaveCount(2);
        requestClientFactory.Requests[0].Should().BeOfType<CheckStock>();
        requestClientFactory.Requests[1].Should().BeOfType<ReserveItem>();
        ctx.Mediator.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoteSend_InMemoryTransport_RoundTripsThroughTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga().UseInMemory().UseRequestClient();
        services.AddInMemoryTransport();
        services.AddFlow<CheckoutState, CheckoutFlow>();

        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var capturedRequests = new List<object>();

        await transport.SubscribeAsync<CheckStock>(async (request, _) =>
        {
            capturedRequests.Add(request);
            await transport.PublishAsync(new StockResult(7, true));
        });

        await transport.SubscribeAsync<ReserveItem>(async (request, _) =>
        {
            capturedRequests.Add(request);
            await transport.PublishAsync("RES-TRANSPORT");
        });

        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<CheckoutState>>();

        var result = await flow.RunAsync(new CheckoutState
        {
            FlowId = "transport-remote-send",
            Sku = "SKU-TRANSPORT",
            Qty = 2
        });

        result.IsSuccess.Should().BeTrue();
        result.State.Should().NotBeNull();
        result.State!.InStock.Should().BeTrue();
        result.State.Qty.Should().Be(7);
        result.State.ReservationId.Should().Be("RES-TRANSPORT");

        capturedRequests.Should().HaveCount(2);
        capturedRequests[0].Should().BeOfType<CheckStock>()
            .Which.Sku.Should().Be("SKU-TRANSPORT");
        capturedRequests[1].Should().BeOfType<ReserveItem>()
            .Which.Should().Match<ReserveItem>(request =>
                request.Sku == "SKU-TRANSPORT" &&
                request.Qty == 7);
    }
}
