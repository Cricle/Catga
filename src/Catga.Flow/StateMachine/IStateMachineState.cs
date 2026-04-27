namespace Catga.Flow.StateMachine;

/// <summary>
/// Marker interface for state machine states.
/// Requires a CurrentState property to track the current state enum value.
/// </summary>
public interface IStateMachineState<TStateEnum> : Dsl.IFlowState
    where TStateEnum : struct, Enum
{
    /// <summary>Current state of the state machine.</summary>
    TStateEnum CurrentState { get; set; }
}
