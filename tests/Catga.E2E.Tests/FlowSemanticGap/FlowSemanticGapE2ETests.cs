using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow;
using Catga.Flow.DependencyInjection;
using Catga.Flow.Dsl;
using Catga.Flow.StateMachine;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catga.E2E.Tests.FlowSemanticGap;

/// <summary>
/// Characterization tests for current flow/state-machine semantic gaps.
/// These assert the current limitation so they can document behavior without touching src.
/// </summary>
public class FlowSemanticGapE2ETests
{
    [Fact]
    public async Task DelayResume_WithoutRegisteredResumePipeline_DoesNotContinuePastDelayedStep()
    {
        var mediator = new RecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new RecordingScheduler();
        var flow = new DelaySemanticGapFlow();
        var executor = new DslFlowExecutor<ScheduledResumeState, DelaySemanticGapFlow>(mediator, store, flow, scheduler);
        var state = new ScheduledResumeState();

        var firstRun = await executor.RunAsync(state);

        var resumeHandler = new DefaultFlowResumeHandler(
            store,
            NullServiceProvider.Instance,
            registrations: [],
            NullLogger<DefaultFlowResumeHandler>.Instance);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var snapshotAfterScheduledResume = await store.GetAsync<ScheduledResumeState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotAfterScheduledResume.Should().NotBeNull();
        snapshotAfterScheduledResume!.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotAfterScheduledResume.Position.CurrentIndex.Should().Be(1);
        mediator.Count<BeforeScheduleCommand>().Should().Be(1);
        mediator.Count<AfterScheduleCommand>().Should().Be(0);
        scheduler.ScheduleCalls.Should().HaveCount(1);
        (await store.GetWaitConditionAsync($"{state.FlowId}-step-1"))!.CompletedCount.Should().Be(1);
    }

    [Fact]
    public async Task ScheduleAtResume_WithoutRegisteredResumePipeline_DoesNotContinuePastScheduledStep()
    {
        var mediator = new RecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new RecordingScheduler();
        var flow = new ScheduleAtSemanticGapFlow();
        var executor = new DslFlowExecutor<ScheduledResumeState, ScheduleAtSemanticGapFlow>(mediator, store, flow, scheduler);
        var state = new ScheduledResumeState
        {
            ResumeAtUtc = DateTime.UtcNow.AddHours(2)
        };

        var firstRun = await executor.RunAsync(state);

        var resumeHandler = new DefaultFlowResumeHandler(
            store,
            NullServiceProvider.Instance,
            registrations: [],
            NullLogger<DefaultFlowResumeHandler>.Instance);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var snapshotAfterScheduledResume = await store.GetAsync<ScheduledResumeState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotAfterScheduledResume.Should().NotBeNull();
        snapshotAfterScheduledResume!.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotAfterScheduledResume.Position.CurrentIndex.Should().Be(1);
        mediator.Count<BeforeScheduleCommand>().Should().Be(1);
        mediator.Count<AfterScheduleCommand>().Should().Be(0);
        scheduler.ScheduleCalls.Should().HaveCount(1);
        (await store.GetWaitConditionAsync($"{state.FlowId}-step-1"))!.CompletedCount.Should().Be(1);
    }

    [Fact]
    public async Task WaitConditionSatisfied_WithoutResumeHandler_DoesNotAutoAdvanceParentFlow()
    {
        var mediator = new RecordingMediator();
        var store = new InMemoryDslFlowStore();
        var flow = new ParentWaitSemanticGapFlow();
        var executor = new DslFlowExecutor<ParentWaitState, ParentWaitSemanticGapFlow>(mediator, store, flow);
        var state = new ParentWaitState();

        var firstRun = await executor.RunAsync(state);

        var handler = new FlowResumeHandler(store);
        var correlationId = $"{state.FlowId}-step-0";

        await handler.HandleAsync(new FlowCompletedEvent("child-a", correlationId, true, null, null));
        await handler.HandleAsync(new FlowCompletedEvent("child-b", correlationId, true, null, null));

        var snapshotBeforeManualResume = await store.GetAsync<ParentWaitState>(state.FlowId!);
        var waitCondition = await store.GetWaitConditionAsync(correlationId);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotBeforeManualResume.Should().NotBeNull();
        snapshotBeforeManualResume!.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotBeforeManualResume.Position.CurrentIndex.Should().Be(0);
        waitCondition.Should().NotBeNull();
        waitCondition!.CompletedCount.Should().Be(2);
        mediator.Count<FinalizeParentCommand>().Should().Be(0);

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeParentCommand>().Should().Be(1);
    }

    [Fact]
    public async Task StateMachine_IsNotMessageDrivenSaga_PublishedEventsDoNotTransitionState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<OrderSagaState, OrderSagaStatus, OrderSagaStateMachine>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<OrderSagaState, OrderSagaStatus, OrderSagaStateMachine>>();

        var orderId = $"order-{Guid.NewGuid():N}";
        await executor.InitializeAsync(orderId, OrderSagaStatus.Pending);

        await mediator.PublishAsync(new PaymentCapturedEvent(orderId));

        var stateAfterPublish = await executor.GetStateAsync(orderId);
        stateAfterPublish.Should().NotBeNull();
        stateAfterPublish!.CurrentState.Should().Be(OrderSagaStatus.Pending);

        var manualResult = await executor.HandleAsync(orderId, new PaymentCapturedEvent(orderId));
        var stateAfterManualHandle = await executor.GetStateAsync(orderId);

        manualResult.Handled.Should().BeTrue();
        manualResult.Transitioned.Should().BeTrue();
        stateAfterManualHandle.Should().NotBeNull();
        stateAfterManualHandle!.CurrentState.Should().Be(OrderSagaStatus.Paid);
    }

    [Fact]
    public async Task StateMachine_WithCorrelatedInitialFactory_PublishedFirstEventSeedsStateAndTransitions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options => options.ForDevelopment());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddStateMachine<EnrollmentSagaState, EnrollmentSagaStatus, EnrollmentSagaStateMachine>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var executor = provider.GetRequiredService<StateMachineExecutor<EnrollmentSagaState, EnrollmentSagaStatus, EnrollmentSagaStateMachine>>();

        await mediator.PublishAsync(new EnrollmentActivatedEvent("enrollment-e2e", "account-e2e", "PAY-E2E"));

        var state = await executor.GetStateAsync("enrollment-e2e");

        state.Should().NotBeNull();
        state!.FlowId.Should().Be("enrollment-e2e");
        state.AccountId.Should().Be("account-e2e");
        state.LastPaymentReference.Should().Be("PAY-E2E");
        state.CurrentState.Should().Be(EnrollmentSagaStatus.Active);
    }

    private sealed class ScheduledResumeState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime ResumeAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => [];
    }

    private sealed class ParentWaitState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString("N");
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => [];
    }

    private sealed class DelaySemanticGapFlow : FlowConfig<ScheduledResumeState>
    {
        protected override void Configure(IFlowBuilder<ScheduledResumeState> flow)
        {
            flow.Send(state => new BeforeScheduleCommand(state.FlowId!));
            flow.Delay(TimeSpan.FromMinutes(5));
            flow.Send(state => new AfterScheduleCommand(state.FlowId!));
        }
    }

    private sealed class ScheduleAtSemanticGapFlow : FlowConfig<ScheduledResumeState>
    {
        protected override void Configure(IFlowBuilder<ScheduledResumeState> flow)
        {
            flow.Send(state => new BeforeScheduleCommand(state.FlowId!));
            flow.ScheduleAt(state => state.ResumeAtUtc);
            flow.Send(state => new AfterScheduleCommand(state.FlowId!));
        }
    }

    private sealed class ParentWaitSemanticGapFlow : FlowConfig<ParentWaitState>
    {
        protected override void Configure(IFlowBuilder<ParentWaitState> flow)
        {
            flow.WhenAll(
                    state => new ChildWorkCommand(state.FlowId!, "child-a"),
                    state => new ChildWorkCommand(state.FlowId!, "child-b"))
                .Timeout(TimeSpan.FromMinutes(5));

            flow.Send(state => new FinalizeParentCommand(state.FlowId!));
        }
    }

    private sealed record BeforeScheduleCommand(string FlowId) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record AfterScheduleCommand(string FlowId) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record ChildWorkCommand(string FlowId, string ChildId) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record FinalizeParentCommand(string FlowId) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed class RecordingMediator : ICatgaMediator
    {
        private readonly List<object> _sentRequests = [];

        public int Count<TRequest>() => _sentRequests.Count(request => request is TRequest);

        public ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            _sentRequests.Add(request!);
            return ValueTask.FromResult(CatgaResult<TResponse>.Success(CreateResponse<TResponse>()));
        }

        public ValueTask<CatgaResult> SendAsync<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _sentRequests.Add(request!);
            return ValueTask.FromResult(CatgaResult.Success());
        }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
            => Task.CompletedTask;

        public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
            IReadOnlyList<TRequest> requests,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            foreach (var request in requests)
                _sentRequests.Add(request!);

            IReadOnlyList<CatgaResult<TResponse>> results = requests
                .Select(_ => CatgaResult<TResponse>.Success(CreateResponse<TResponse>()))
                .ToList();

            return ValueTask.FromResult(results);
        }

        public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
            IAsyncEnumerable<TRequest> requests,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            await foreach (var request in requests.WithCancellation(cancellationToken))
            {
                _sentRequests.Add(request!);
                yield return CatgaResult<TResponse>.Success(CreateResponse<TResponse>());
            }
        }

        public Task PublishBatchAsync<TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default)
            where TEvent : IEvent
            => Task.CompletedTask;

        private static TResponse CreateResponse<TResponse>()
        {
            if (typeof(TResponse) == typeof(string))
                return (TResponse)(object)"ok";

            if (typeof(TResponse) == typeof(bool))
                return (TResponse)(object)true;

            return Activator.CreateInstance<TResponse>();
        }
    }

    private sealed class RecordingScheduler : IFlowScheduler
    {
        public List<(string FlowId, string StateId, DateTimeOffset ResumeAt)> ScheduleCalls { get; } = [];

        public ValueTask<string> ScheduleResumeAsync(
            string flowId,
            string stateId,
            DateTimeOffset resumeAt,
            CancellationToken ct = default)
        {
            ScheduleCalls.Add((flowId, stateId, resumeAt));
            return ValueTask.FromResult($"schedule-{ScheduleCalls.Count}");
        }

        public ValueTask<bool> CancelScheduledResumeAsync(string scheduleId, CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static NullServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }

    private enum OrderSagaStatus
    {
        Pending,
        Paid
    }

    private sealed class OrderSagaState : IStateMachineState<OrderSagaStatus>
    {
        public string? FlowId { get; set; }
        public OrderSagaStatus CurrentState { get; set; } = OrderSagaStatus.Pending;
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => [];
    }

    private sealed record PaymentCapturedEvent(string OrderId) : IEvent
    {
        public long MessageId => 0;
    }

    private sealed class OrderSagaStateMachine : StateMachineConfig<OrderSagaState, OrderSagaStatus>
    {
        protected override void Configure()
        {
            State(OrderSagaStatus.Pending)
                .On<PaymentCapturedEvent>()
                .TransitionTo(OrderSagaStatus.Paid);
        }
    }

    private enum EnrollmentSagaStatus
    {
        None = 0,
        AwaitingActivation = 1,
        Active = 2
    }

    private sealed class EnrollmentSagaState : IStateMachineState<EnrollmentSagaStatus>
    {
        public string? FlowId { get; set; }
        public EnrollmentSagaStatus CurrentState { get; set; } = EnrollmentSagaStatus.None;
        public string? AccountId { get; set; }
        public string? LastPaymentReference { get; set; }
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => [];
    }

    private sealed record EnrollmentActivatedEvent(string EnrollmentId, string AccountId, string PaymentReference) : IEvent
    {
        public long MessageId => 0;
    }

    private sealed class EnrollmentSagaStateMachine : StateMachineConfig<EnrollmentSagaState, EnrollmentSagaStatus>
    {
        protected override void Configure()
        {
            State(EnrollmentSagaStatus.AwaitingActivation)
                .On<EnrollmentActivatedEvent>()
                .StartsNew(
                    e => e.EnrollmentId,
                    (e, instanceId) => new EnrollmentSagaState
                    {
                        FlowId = instanceId,
                        AccountId = e.AccountId
                    })
                .Execute((s, e) => s.LastPaymentReference = e.PaymentReference)
                .TransitionTo(EnrollmentSagaStatus.Active);
        }
    }
}
