using Microsoft.Extensions.Logging;

namespace Catga.Flow.Dsl;

/// <summary>
/// Default implementation of IFlowResumeHandler that resumes flows from their stored snapshots.
/// </summary>
public sealed class DefaultFlowResumeHandler : IFlowResumeHandler
{
    private readonly IDslFlowStore _store;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IReadOnlyList<IFlowResumeRegistration> _registrations;
    private readonly ILogger<DefaultFlowResumeHandler> _logger;

    public DefaultFlowResumeHandler(
        IDslFlowStore store,
        ILogger<DefaultFlowResumeHandler> logger)
        : this(store, serviceProvider: null, registrations: [], logger)
    {
    }

    public DefaultFlowResumeHandler(
        IDslFlowStore store,
        IServiceProvider? serviceProvider,
        IEnumerable<IFlowResumeRegistration>? registrations,
        ILogger<DefaultFlowResumeHandler> logger)
    {
        _store = store;
        _serviceProvider = serviceProvider;
        _registrations = registrations?.ToArray() ?? [];
        _logger = logger;
    }

    public async ValueTask ResumeFlowAsync(string flowId, string stateId, CancellationToken ct = default)
    {
        _logger.LogInformation("Attempting to resume flow {FlowId}", flowId);

        var waitConditions = await FindWaitConditionsAsync(flowId, stateId, ct);
        if (waitConditions.Count == 0)
        {
            _logger.LogWarning("No wait condition found for flow {FlowId}, cannot resume", flowId);
            return;
        }

        foreach (var waitCondition in waitConditions.Where(condition => condition.CompletedCount < condition.ExpectedCount))
        {
            var completed = waitCondition with { CompletedCount = waitCondition.ExpectedCount };
            await _store.UpdateWaitConditionAsync(completed.CorrelationId, completed, ct);
        }

        var dispatchCondition = waitConditions[0];

        if (_serviceProvider == null || _registrations.Count == 0)
        {
            _logger.LogInformation(
                "Flow {FlowId} wait condition(s) updated for step {Step}, but no flow registration is available for automatic resume",
                flowId,
                dispatchCondition.Step);
            return;
        }

        var registration = _registrations.FirstOrDefault(r => r.MatchesFlowType(dispatchCondition.FlowType));
        if (registration == null)
        {
            _logger.LogWarning(
                "No flow resume registration found for flow {FlowId} with flow type {FlowType}",
                flowId,
                dispatchCondition.FlowType);
            return;
        }

        _logger.LogInformation(
            "Dispatching flow resume for flow {FlowId} at step {Step} using flow type {FlowType}",
            flowId,
            dispatchCondition.Step,
            dispatchCondition.FlowType);

        await registration.ResumeAsync(_serviceProvider, flowId, ct);
    }

    private async Task<IReadOnlyList<WaitCondition>> FindWaitConditionsAsync(string flowId, string stateId, CancellationToken ct)
    {
        var timedOut = await _store.GetTimedOutWaitConditionsAsync(ct);
        var matchingTimedOut = timedOut
            .Where(condition => condition.FlowId == flowId)
            .OrderBy(condition => condition.CreatedAt)
            .ToList();

        if (!string.Equals(stateId, flowId, StringComparison.Ordinal))
        {
            var exact = matchingTimedOut.FirstOrDefault(condition => condition.CorrelationId == stateId)
                ?? await _store.GetWaitConditionAsync(stateId, ct);

            return exact != null && exact.FlowId == flowId
                ? [exact]
                : [];
        }

        var active = await _store.GetWaitConditionsByFlowAsync(flowId, ct);
        return matchingTimedOut
            .Concat(active)
            .GroupBy(condition => condition.CorrelationId)
            .Select(group => group.First())
            .OrderBy(condition => condition.CreatedAt)
            .ToList();
    }
}
