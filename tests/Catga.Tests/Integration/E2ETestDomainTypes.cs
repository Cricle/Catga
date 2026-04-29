using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Flow.StateMachine;

namespace Catga.Tests.Integration;

// ── Redis E2E domain types ────────────────────────────────────────────────────

public enum RedisPaymentStatus { Pending, Authorized, Captured, Failed }

public class RedisPaymentState : IStateMachineState<RedisPaymentStatus>
{
    public string? FlowId { get; set; }
    public RedisPaymentStatus CurrentState { get; set; } = RedisPaymentStatus.Pending;
    public string? AuthCode { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record RedisPaymentAuthorized(string AuthCode) : IEvent { public long MessageId { get; init; } }
public record RedisPaymentCaptured : IEvent { public long MessageId { get; init; } }

public class RedisPaymentMachine : StateMachineConfig<RedisPaymentState, RedisPaymentStatus>
{
    protected override void Configure()
    {
        State(RedisPaymentStatus.Pending)
            .On<RedisPaymentAuthorized>()
                .Execute((s, e) => s.AuthCode = e.AuthCode)
                .TransitionTo(RedisPaymentStatus.Authorized);

        State(RedisPaymentStatus.Authorized)
            .On<RedisPaymentCaptured>()
                .TransitionTo(RedisPaymentStatus.Captured);
    }
}

// ── NATS E2E domain types ─────────────────────────────────────────────────────

public enum NatsTicketStatus { Open, InProgress, Resolved }

public class NatsTicketState : IStateMachineState<NatsTicketStatus>
{
    public string? FlowId { get; set; }
    public NatsTicketStatus CurrentState { get; set; } = NatsTicketStatus.Open;
    public string? AssignedTo { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
}

public record NatsTicketAssigned(string Agent) : IEvent { public long MessageId { get; init; } }
public record NatsTicketResolved : IEvent { public long MessageId { get; init; } }

public class NatsTicketMachine : StateMachineConfig<NatsTicketState, NatsTicketStatus>
{
    protected override void Configure()
    {
        State(NatsTicketStatus.Open)
            .On<NatsTicketAssigned>()
                .Execute((s, e) => s.AssignedTo = e.Agent)
                .TransitionTo(NatsTicketStatus.InProgress);

        State(NatsTicketStatus.InProgress)
            .On<NatsTicketResolved>()
                .TransitionTo(NatsTicketStatus.Resolved);
    }
}
