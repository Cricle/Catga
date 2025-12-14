namespace Catga.Flow;

/// <summary>
/// Service interface for resuming flows from scheduled jobs.
/// Implement this interface to handle flow resumption from external schedulers.
/// </summary>
public interface IFlowResumeHandler
{
    /// <summary>
    /// Resume a suspended flow.
    /// </summary>
    /// <param name="flowId">The flow instance ID.</param>
    /// <param name="stateId">The state ID (same as flowId for most cases).</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ResumeFlowAsync(string flowId, string stateId, CancellationToken ct = default);
}
