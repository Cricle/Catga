using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Flow.Dsl;
using Catga.Flow.DependencyInjection;
using Catga.Flow.StateMachine;
using Catga.Persistence.InMemory.Flow;
using Catga.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow;

// ── Domain types ──────────────────────────────────────────────────────────────

public enum OrderStatus { Pending, Paid, Shipped, Delivered, Cancelled }

public class OrderState : IStateMachineState<OrderStatus>
{
    public string? FlowId { get; set; }
    public OrderStatus CurrentState { get; set; } = OrderStatus.Pending;
    public string? PaymentId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CancelReason { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record OrderPaid(string PaymentId) : IEvent { public long MessageId { get; init; } }
public record OrderShipped(string TrackingNumber) : IEvent { public long MessageId { get; init; } }
public record OrderDelivered : IEvent { public long MessageId { get; init; } }
public record OrderCancelled(string Reason) : IEvent { public long MessageId { get; init; } }
public record UnknownEvent : IEvent { public long MessageId { get; init; } }

public class OrderStateMachine : StateMachineConfig<OrderState, OrderStatus>
{
    protected override void Configure()
    {
        State(OrderStatus.Pending)
            .On<OrderPaid>()
                .Execute((s, e, _) => { s.PaymentId = e.PaymentId; return ValueTask.CompletedTask; })
                .TransitionTo(OrderStatus.Paid)
            .And()
            .On<OrderCancelled>()
                .Execute((s, e) => s.CancelReason = e.Reason)
                .TransitionTo(OrderStatus.Cancelled);

        State(OrderStatus.Paid)
            .On<OrderShipped>()
                .Execute((s, e) => s.TrackingNumber = e.TrackingNumber)
                .TransitionTo(OrderStatus.Shipped)
            .And()
            .On<OrderCancelled>()
                .TransitionTo(OrderStatus.Cancelled);

        State(OrderStatus.Shipped)
            .On<OrderDelivered>()
                .TransitionTo(OrderStatus.Delivered);
    }
}

// ── StateMachineConfig unit tests ─────────────────────────────────────────────

public class StateMachineConfigTests
{
    private readonly OrderStateMachine _sm = new();

    [Fact]
    public void Build_RegistersAllStates()
    {
        _sm.Build();
        _sm.States.Should().ContainKey(OrderStatus.Pending);
        _sm.States.Should().ContainKey(OrderStatus.Paid);
        _sm.States.Should().ContainKey(OrderStatus.Shipped);
    }

    [Fact]
    public void Build_IsIdempotent()
    {
        _sm.Build();
        _sm.Build(); // second call should not throw or duplicate
        _sm.States.Should().HaveCount(3);
    }

    [Fact]
    public void CanHandle_ValidTransition_ReturnsTrue()
    {
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        _sm.CanHandle(state, typeof(OrderPaid)).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_InvalidTransition_ReturnsFalse()
    {
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        _sm.CanHandle(state, typeof(OrderShipped)).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_UnknownState_ReturnsFalse()
    {
        var state = new OrderState { CurrentState = OrderStatus.Delivered };
        _sm.CanHandle(state, typeof(OrderPaid)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessEvent_ValidTransition_ChangesState()
    {
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        var newState = await _sm.ProcessEventAsync(state, new OrderPaid("PAY-001"));
        newState.Should().Be(OrderStatus.Paid);
        state.CurrentState.Should().Be(OrderStatus.Paid);
        state.PaymentId.Should().Be("PAY-001");
    }

    [Fact]
    public async Task ProcessEvent_ExecutesAction()
    {
        var state = new OrderState { CurrentState = OrderStatus.Paid };
        await _sm.ProcessEventAsync(state, new OrderShipped("TRACK-123"));
        state.TrackingNumber.Should().Be("TRACK-123");
        state.CurrentState.Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public async Task ProcessEvent_NoMatchingTransition_StateUnchanged()
    {
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        var newState = await _sm.ProcessEventAsync(state, new OrderShipped("X"));
        newState.Should().Be(OrderStatus.Pending);
        state.CurrentState.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task ProcessEvent_UnknownState_StateUnchanged()
    {
        var state = new OrderState { CurrentState = OrderStatus.Delivered };
        var newState = await _sm.ProcessEventAsync(state, new OrderPaid("X"));
        newState.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task ProcessEvent_FullLifecycle_PendingToCancelledViaCancel()
    {
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        await _sm.ProcessEventAsync(state, new OrderCancelled("Out of stock"));
        state.CurrentState.Should().Be(OrderStatus.Cancelled);
        state.CancelReason.Should().Be("Out of stock");
    }

    [Fact]
    public async Task ProcessEvent_FullLifecycle_PendingToDelivered()
    {
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        await _sm.ProcessEventAsync(state, new OrderPaid("PAY-1"));
        await _sm.ProcessEventAsync(state, new OrderShipped("TRK-1"));
        await _sm.ProcessEventAsync(state, new OrderDelivered());
        state.CurrentState.Should().Be(OrderStatus.Delivered);
    }
}

// ── Guard condition tests ─────────────────────────────────────────────────────

public enum TicketStatus { Open, InProgress, Resolved, Closed }

public class TicketState : IStateMachineState<TicketStatus>
{
    public string? FlowId { get; set; }
    public TicketStatus CurrentState { get; set; } = TicketStatus.Open;
    public int Priority { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record TicketAssigned(int Priority) : IEvent { public long MessageId { get; init; } }
public record TicketResolved : IEvent { public long MessageId { get; init; } }

public class TicketStateMachine : StateMachineConfig<TicketState, TicketStatus>
{
    protected override void Configure()
    {
        State(TicketStatus.Open)
            .On<TicketAssigned>()
                .When((s, e) => e.Priority >= 1) // guard: only assign if priority valid
                .Execute((s, e) => s.Priority = e.Priority)
                .TransitionTo(TicketStatus.InProgress);

        State(TicketStatus.InProgress)
            .On<TicketResolved>()
                .TransitionTo(TicketStatus.Resolved);
    }
}

public class StateMachineGuardTests
{
    [Fact]
    public async Task Guard_WhenConditionTrue_TransitionOccurs()
    {
        var sm = new TicketStateMachine();
        var state = new TicketState { CurrentState = TicketStatus.Open };
        await sm.ProcessEventAsync(state, new TicketAssigned(Priority: 3));
        state.CurrentState.Should().Be(TicketStatus.InProgress);
        state.Priority.Should().Be(3);
    }

    [Fact]
    public async Task Guard_WhenConditionFalse_NoTransition()
    {
        var sm = new TicketStateMachine();
        var state = new TicketState { CurrentState = TicketStatus.Open };
        await sm.ProcessEventAsync(state, new TicketAssigned(Priority: 0)); // fails guard
        state.CurrentState.Should().Be(TicketStatus.Open);
    }
}

// ── OnEnter/OnExit tests ──────────────────────────────────────────────────────

public class OnEnterExitStateMachine : StateMachineConfig<OrderState, OrderStatus>
{
    public List<string> Log { get; } = [];

    protected override void Configure()
    {
        State(OrderStatus.Pending)
            .OnExit(async (s, _) => { Log.Add("exit:Pending"); await Task.CompletedTask; })
            .On<OrderPaid>().TransitionTo(OrderStatus.Paid);

        State(OrderStatus.Paid)
            .OnEnter(async (s, _) => { Log.Add("enter:Paid"); await Task.CompletedTask; });
    }
}

public class StateMachineOnEnterExitTests
{
    [Fact]
    public async Task OnExit_CalledWhenLeavingState()
    {
        var sm = new OnEnterExitStateMachine();
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        await sm.ProcessEventAsync(state, new OrderPaid("P1"));
        sm.Log.Should().Contain("exit:Pending");
    }

    [Fact]
    public async Task OnEnter_CalledWhenEnteringState()
    {
        var sm = new OnEnterExitStateMachine();
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        await sm.ProcessEventAsync(state, new OrderPaid("P1"));
        sm.Log.Should().Contain("enter:Paid");
    }

    [Fact]
    public async Task OnExitThenOnEnter_OrderIsCorrect()
    {
        var sm = new OnEnterExitStateMachine();
        var state = new OrderState { CurrentState = OrderStatus.Pending };
        await sm.ProcessEventAsync(state, new OrderPaid("P1"));
        sm.Log.Should().Equal("exit:Pending", "enter:Paid");
    }
}

// ── StateMachineExecutor E2E tests ────────────────────────────────────────────

public class StateMachineExecutorTests
{
    private static StateMachineExecutor<OrderState, OrderStatus, OrderStateMachine> CreateExecutor()
        => new(new InMemoryDslFlowStore());

    [Fact]
    public async Task InitializeAsync_CreatesInstance()
    {
        var executor = CreateExecutor();
        var state = await executor.InitializeAsync("order-1", OrderStatus.Pending);
        state.CurrentState.Should().Be(OrderStatus.Pending);
        state.FlowId.Should().Be("order-1");
    }

    [Fact]
    public async Task HandleAsync_ValidEvent_TransitionsState()
    {
        var executor = CreateExecutor();
        await executor.InitializeAsync("order-2", OrderStatus.Pending);

        var result = await executor.HandleAsync("order-2", new OrderPaid("PAY-2"));

        result.Handled.Should().BeTrue();
        result.Transitioned.Should().BeTrue();
        result.PreviousState.Should().Be(OrderStatus.Pending);
        result.CurrentState.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task HandleAsync_InvalidEvent_NotHandled()
    {
        var executor = CreateExecutor();
        await executor.InitializeAsync("order-3", OrderStatus.Pending);

        var result = await executor.HandleAsync("order-3", new OrderShipped("TRK"));

        result.Handled.Should().BeFalse();
        result.Transitioned.Should().BeFalse();
        result.CurrentState.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task HandleAsync_PersistsState()
    {
        var executor = CreateExecutor();
        await executor.InitializeAsync("order-4", OrderStatus.Pending);
        await executor.HandleAsync("order-4", new OrderPaid("PAY-4"));

        var state = await executor.GetStateAsync("order-4");
        state.Should().NotBeNull();
        state!.CurrentState.Should().Be(OrderStatus.Paid);
        state.PaymentId.Should().Be("PAY-4");
    }

    [Fact]
    public async Task HandleAsync_FullLifecycle_AllTransitions()
    {
        var executor = CreateExecutor();
        await executor.InitializeAsync("order-5", OrderStatus.Pending);

        var r1 = await executor.HandleAsync("order-5", new OrderPaid("PAY-5"));
        var r2 = await executor.HandleAsync("order-5", new OrderShipped("TRK-5"));
        var r3 = await executor.HandleAsync("order-5", new OrderDelivered());

        r1.CurrentState.Should().Be(OrderStatus.Paid);
        r2.CurrentState.Should().Be(OrderStatus.Shipped);
        r3.CurrentState.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task HandleAsync_UnknownInstance_CreatesNewState()
    {
        var executor = CreateExecutor();
        // No InitializeAsync — HandleAsync should create state on first event
        var result = await executor.HandleAsync("order-new", new OrderPaid("PAY-NEW"));
        // Pending is default, so OrderPaid should transition
        result.Handled.Should().BeTrue();
    }

    [Fact]
    public async Task GetStateAsync_NonExistentInstance_ReturnsNull()
    {
        var executor = CreateExecutor();
        var state = await executor.GetStateAsync("nonexistent");
        state.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithInitialConfig_UsesProvidedConfig()
    {
        var config = new OrderStateMachine();
        var executor = new StateMachineExecutor<OrderState, OrderStatus, OrderStateMachine>(
            new InMemoryDslFlowStore(), config);

        await executor.InitializeAsync("order-cfg", OrderStatus.Pending);
        var result = await executor.HandleAsync("order-cfg", new OrderPaid("PAY-CFG"));
        result.Handled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithStrictOptimisticStore_PersistsTransition()
    {
        var store = new StrictOptimisticDslFlowStore();
        var executor = new StateMachineExecutor<OrderState, OrderStatus, OrderStateMachine>(store);

        await executor.InitializeAsync("order-strict", OrderStatus.Pending);
        var result = await executor.HandleAsync("order-strict", new OrderPaid("PAY-STRICT"));
        var snapshot = await store.GetAsync<OrderState>("order-strict");

        result.Handled.Should().BeTrue();
        store.RejectedUpdateCount.Should().Be(0);
        snapshot.Should().NotBeNull();
        snapshot!.State.CurrentState.Should().Be(OrderStatus.Paid);
        snapshot.State.PaymentId.Should().Be("PAY-STRICT");
        snapshot.Version.Should().Be(2);
    }
}

public enum OrderSagaBridgeStatus { Pending, Paid }

public class OrderSagaBridgeState : IStateMachineState<OrderSagaBridgeStatus>
{
    public string? FlowId { get; set; }
    public OrderSagaBridgeStatus CurrentState { get; set; } = OrderSagaBridgeStatus.Pending;
    public string? LastPaymentId { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record PaymentCapturedForBridge(string OrderId, string PaymentId) : IEvent
{
    public long MessageId { get; init; }
}

public record PaymentCapturedForAuto(string OrderId, string PaymentId) : IEvent
{
    public long MessageId { get; init; }
}

public class OrderSagaBridgeStateMachine : StateMachineConfig<OrderSagaBridgeState, OrderSagaBridgeStatus>
{
    protected override void Configure()
    {
        State(OrderSagaBridgeStatus.Pending)
            .On<PaymentCapturedForBridge>()
                .Execute((s, e) => s.LastPaymentId = e.PaymentId)
                .TransitionTo(OrderSagaBridgeStatus.Paid);
    }
}

public class OrderSagaAutoBridgeStateMachine : StateMachineConfig<OrderSagaBridgeState, OrderSagaBridgeStatus>
{
    protected override void Configure()
    {
        State(OrderSagaBridgeStatus.Pending)
            .On<PaymentCapturedForAuto>()
                .CorrelateById(e => e.OrderId)
                .Execute((s, e) => s.LastPaymentId = e.PaymentId)
                .TransitionTo(OrderSagaBridgeStatus.Paid);
    }
}

public class StateMachineMessageBridgeTests
{
    [Fact]
    public async Task PublishAsync_WithRegisteredStateMachineEventBridge_TransitionsState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<OrderSagaBridgeState, OrderSagaBridgeStatus, OrderSagaBridgeStateMachine>();
        services.AddStateMachineEvent<OrderSagaBridgeState, OrderSagaBridgeStatus, OrderSagaBridgeStateMachine, PaymentCapturedForBridge>(
            e => e.OrderId);

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<OrderSagaBridgeState, OrderSagaBridgeStatus, OrderSagaBridgeStateMachine>>();

        var orderId = $"order-{Guid.NewGuid():N}";
        await executor.InitializeAsync(orderId, OrderSagaBridgeStatus.Pending);

        await mediator.PublishAsync(new PaymentCapturedForBridge(orderId, "PAY-BRIDGE"));

        var state = await executor.GetStateAsync(orderId);

        state.Should().NotBeNull();
        state!.CurrentState.Should().Be(OrderSagaBridgeStatus.Paid);
        state.LastPaymentId.Should().Be("PAY-BRIDGE");
    }

    [Fact]
    public async Task PublishAsync_WithCorrelatedStateMachineRegistration_TransitionsState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<OrderSagaBridgeState, OrderSagaBridgeStatus, OrderSagaAutoBridgeStateMachine>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<OrderSagaBridgeState, OrderSagaBridgeStatus, OrderSagaAutoBridgeStateMachine>>();

        var orderId = $"order-{Guid.NewGuid():N}";
        await executor.InitializeAsync(orderId, OrderSagaBridgeStatus.Pending);

        await mediator.PublishAsync(new PaymentCapturedForAuto(orderId, "PAY-AUTO"));

        var state = await executor.GetStateAsync(orderId);

        state.Should().NotBeNull();
        state!.CurrentState.Should().Be(OrderSagaBridgeStatus.Paid);
        state.LastPaymentId.Should().Be("PAY-AUTO");
    }
}

public enum SubscriptionStatus { None = 0, WaitingForPayment = 1, Active = 2 }

public class SubscriptionState : IStateMachineState<SubscriptionStatus>
{
    public string? FlowId { get; set; }
    public SubscriptionStatus CurrentState { get; set; } = SubscriptionStatus.None;
    public string? LastInvoiceId { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record SubscriptionPaid(string SubscriptionId, string InvoiceId) : IEvent
{
    public long MessageId { get; init; }
}

public class SubscriptionStateMachine : StateMachineConfig<SubscriptionState, SubscriptionStatus>
{
    protected override void Configure()
    {
        Initially(SubscriptionStatus.WaitingForPayment);
        CorrelateById<SubscriptionPaid>(e => e.SubscriptionId);

        State(SubscriptionStatus.WaitingForPayment)
            .On<SubscriptionPaid>()
                .Execute((s, e) => s.LastInvoiceId = e.InvoiceId)
                .TransitionTo(SubscriptionStatus.Active);
    }
}

public class StateMachineInitialStateTests
{
    [Fact]
    public async Task HandleAsync_UnknownInstance_UsesConfiguredInitialState()
    {
        var executor = new StateMachineExecutor<SubscriptionState, SubscriptionStatus, SubscriptionStateMachine>(
            new InMemoryDslFlowStore());

        var result = await executor.HandleAsync("subscription-1", new SubscriptionPaid("subscription-1", "INV-001"));
        var state = await executor.GetStateAsync("subscription-1");

        result.Handled.Should().BeTrue();
        result.PreviousState.Should().Be(SubscriptionStatus.WaitingForPayment);
        result.CurrentState.Should().Be(SubscriptionStatus.Active);
        state.Should().NotBeNull();
        state!.CurrentState.Should().Be(SubscriptionStatus.Active);
        state.LastInvoiceId.Should().Be("INV-001");
    }

    [Fact]
    public async Task PublishAsync_UnknownInstance_UsesConfiguredInitialState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<SubscriptionState, SubscriptionStatus, SubscriptionStateMachine>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<SubscriptionState, SubscriptionStatus, SubscriptionStateMachine>>();

        await mediator.PublishAsync(new SubscriptionPaid("subscription-2", "INV-002"));

        var state = await executor.GetStateAsync("subscription-2");

        state.Should().NotBeNull();
        state!.CurrentState.Should().Be(SubscriptionStatus.Active);
        state.LastInvoiceId.Should().Be("INV-002");
    }
}

public enum EnrollmentStatus { None = 0, AwaitingActivation = 1, Active = 2 }

public class EnrollmentState : IStateMachineState<EnrollmentStatus>
{
    public string? FlowId { get; set; }
    public EnrollmentStatus CurrentState { get; set; } = EnrollmentStatus.None;
    public string? AccountId { get; set; }
    public string? LastPaymentReference { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record EnrollmentPaymentCaptured(string EnrollmentId, string AccountId, string PaymentReference) : IEvent
{
    public long MessageId { get; init; }
}

public class EnrollmentStateMachine : StateMachineConfig<EnrollmentState, EnrollmentStatus>
{
    protected override void Configure()
    {
        Initially(EnrollmentStatus.AwaitingActivation);
        CreateInstanceFrom<EnrollmentPaymentCaptured>((e, instanceId) => new EnrollmentState
        {
            FlowId = instanceId,
            AccountId = e.AccountId
        });
        CorrelateById<EnrollmentPaymentCaptured>(e => e.EnrollmentId);

        State(EnrollmentStatus.AwaitingActivation)
            .On<EnrollmentPaymentCaptured>()
                .Execute((s, e) => s.LastPaymentReference = e.PaymentReference)
                .TransitionTo(EnrollmentStatus.Active);
    }
}

public class StateMachineInitialInstanceFactoryTests
{
    [Fact]
    public async Task HandleAsync_UnknownInstance_UsesEventFactoryToSeedState()
    {
        var executor = new StateMachineExecutor<EnrollmentState, EnrollmentStatus, EnrollmentStateMachine>(
            new InMemoryDslFlowStore());

        var result = await executor.HandleAsync(
            "enrollment-1",
            new EnrollmentPaymentCaptured("enrollment-1", "account-1", "PAY-001"));
        var state = await executor.GetStateAsync("enrollment-1");

        result.Handled.Should().BeTrue();
        result.PreviousState.Should().Be(EnrollmentStatus.AwaitingActivation);
        result.CurrentState.Should().Be(EnrollmentStatus.Active);
        state.Should().NotBeNull();
        state!.FlowId.Should().Be("enrollment-1");
        state.AccountId.Should().Be("account-1");
        state.LastPaymentReference.Should().Be("PAY-001");
        state.CurrentState.Should().Be(EnrollmentStatus.Active);
    }

    [Fact]
    public async Task PublishAsync_UnknownInstance_UsesEventFactoryToSeedState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<EnrollmentState, EnrollmentStatus, EnrollmentStateMachine>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<EnrollmentState, EnrollmentStatus, EnrollmentStateMachine>>();

        await mediator.PublishAsync(new EnrollmentPaymentCaptured("enrollment-2", "account-2", "PAY-002"));

        var state = await executor.GetStateAsync("enrollment-2");

        state.Should().NotBeNull();
        state!.FlowId.Should().Be("enrollment-2");
        state.AccountId.Should().Be("account-2");
        state.LastPaymentReference.Should().Be("PAY-002");
        state.CurrentState.Should().Be(EnrollmentStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_WithStrictOptimisticStore_PersistsFactoryCreatedInstance()
    {
        var store = new StrictOptimisticDslFlowStore();
        var executor = new StateMachineExecutor<EnrollmentState, EnrollmentStatus, EnrollmentStateMachine>(store);

        var result = await executor.HandleAsync(
            "enrollment-strict",
            new EnrollmentPaymentCaptured("enrollment-strict", "account-strict", "PAY-STRICT"));
        var snapshot = await store.GetAsync<EnrollmentState>("enrollment-strict");

        result.Handled.Should().BeTrue();
        store.RejectedUpdateCount.Should().Be(0);
        snapshot.Should().NotBeNull();
        snapshot!.State.FlowId.Should().Be("enrollment-strict");
        snapshot.State.AccountId.Should().Be("account-strict");
        snapshot.State.LastPaymentReference.Should().Be("PAY-STRICT");
        snapshot.State.CurrentState.Should().Be(EnrollmentStatus.Active);
        snapshot.Version.Should().Be(1);
    }
}

public enum SignupStatus { AwaitingPayment = 1, Active = 2 }

public class SignupState : IStateMachineState<SignupStatus>
{
    public string? FlowId { get; set; }
    public SignupStatus CurrentState { get; set; }
    public string? Email { get; set; }
    public string? AccountId { get; set; }
    public string? LastPaymentReference { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record SignupStarted(string SignupId, string Email) : IEvent
{
    public long MessageId { get; init; }
}

public record SignupPaymentCaptured(string SignupId, string AccountId, string PaymentReference) : IEvent
{
    public long MessageId { get; init; }
}

public class SignupStateMachine : StateMachineConfig<SignupState, SignupStatus>
{
    protected override void Configure()
    {
        State(SignupStatus.AwaitingPayment)
            .On<SignupStarted>()
                .StartsNew(e => e.SignupId, e => new SignupState { Email = e.Email })
            .And()
            .On<SignupPaymentCaptured>()
                .StartsNew(
                    e => e.SignupId,
                    (e, instanceId) => new SignupState
                    {
                        FlowId = instanceId,
                        AccountId = e.AccountId
                    })
                .Execute((s, e) => s.LastPaymentReference = e.PaymentReference)
                .TransitionTo(SignupStatus.Active);
    }
}

public class StateMachineErgonomicApiTests
{
    [Fact]
    public async Task HandleAsync_UnknownInstance_WithStartsNewOnly_PersistsCreatedState()
    {
        var executor = new StateMachineExecutor<SignupState, SignupStatus, SignupStateMachine>(
            new InMemoryDslFlowStore());

        var result = await executor.HandleAsync("signup-1", new SignupStarted("signup-1", "user@example.com"));
        var state = await executor.GetStateAsync("signup-1");

        result.Handled.Should().BeTrue();
        result.Transitioned.Should().BeFalse();
        result.CurrentState.Should().Be(SignupStatus.AwaitingPayment);
        state.Should().NotBeNull();
        state!.FlowId.Should().Be("signup-1");
        state.Email.Should().Be("user@example.com");
        state.CurrentState.Should().Be(SignupStatus.AwaitingPayment);
    }

    [Fact]
    public async Task PublishAsync_UnknownInstance_WithStartsNew_SeedsStateAndTransitions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<SignupState, SignupStatus, SignupStateMachine>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<SignupState, SignupStatus, SignupStateMachine>>();

        await mediator.PublishAsync(new SignupPaymentCaptured("signup-2", "account-2", "PAY-002"));

        var state = await executor.GetStateAsync("signup-2");

        state.Should().NotBeNull();
        state!.FlowId.Should().Be("signup-2");
        state.AccountId.Should().Be("account-2");
        state.LastPaymentReference.Should().Be("PAY-002");
        state.CurrentState.Should().Be(SignupStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_WithStartsNew_AndStrictOptimisticStore_PersistsCreatedState()
    {
        var store = new StrictOptimisticDslFlowStore();
        var executor = new StateMachineExecutor<SignupState, SignupStatus, SignupStateMachine>(store);

        var result = await executor.HandleAsync(
            "signup-strict",
            new SignupPaymentCaptured("signup-strict", "account-strict", "PAY-STRICT"));
        var snapshot = await store.GetAsync<SignupState>("signup-strict");

        result.Handled.Should().BeTrue();
        store.RejectedUpdateCount.Should().Be(0);
        snapshot.Should().NotBeNull();
        snapshot!.State.FlowId.Should().Be("signup-strict");
        snapshot.State.AccountId.Should().Be("account-strict");
        snapshot.State.LastPaymentReference.Should().Be("PAY-STRICT");
        snapshot.State.CurrentState.Should().Be(SignupStatus.Active);
        snapshot.Version.Should().Be(1);
    }
}

// ── StateMachineResult tests ──────────────────────────────────────────────────

public class StateMachineResultTests
{
    [Fact]
    public void Transitioned_WhenStateChanged_IsTrue()
    {
        var result = new StateMachineResult<OrderStatus>("id", OrderStatus.Pending, OrderStatus.Paid, true);
        result.Transitioned.Should().BeTrue();
    }

    [Fact]
    public void Transitioned_WhenStateSame_IsFalse()
    {
        var result = new StateMachineResult<OrderStatus>("id", OrderStatus.Pending, OrderStatus.Pending, false);
        result.Transitioned.Should().BeFalse();
    }

    [Fact]
    public void Handled_False_WhenNoTransition()
    {
        var result = new StateMachineResult<OrderStatus>("id", OrderStatus.Pending, OrderStatus.Pending, false);
        result.Handled.Should().BeFalse();
    }
}

// ── DI registration tests ─────────────────────────────────────────────────────

public class StateMachineDiTests
{
    [Fact]
    public void AddStateMachine_RegistersExecutor()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<OrderState, OrderStatus, OrderStateMachine>();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetService<StateMachineExecutor<OrderState, OrderStatus, OrderStateMachine>>();
        executor.Should().NotBeNull();
    }

    [Fact]
    public void AddStateMachine_RegistersConfig()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<OrderState, OrderStatus, OrderStateMachine>();

        var sp = services.BuildServiceProvider();
        sp.GetService<OrderStateMachine>().Should().NotBeNull();
    }
}
