using Microsoft.Extensions.Logging;

namespace Catga.Saga;

/// <summary>
/// Executes sagas with automatic compensation on failure
/// </summary>
public sealed class SagaExecutor
{
    private readonly ILogger<SagaExecutor> _logger;

    public SagaExecutor(ILogger<SagaExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute a saga
    /// </summary>
    public async ValueTask<SagaResult> ExecuteAsync(
        ISaga saga,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saga);

        _logger.LogInformation(
            "Starting saga {SagaId} with {StepCount} steps",
            saga.SagaId,
            saga.Steps.Count);

        var executedSteps = new List<ISagaStep>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Execute each step in order
            foreach (var step in saga.Steps)
            {
                _logger.LogInformation(
                    "Executing saga step: {SagaId}/{StepName}",
                    saga.SagaId,
                    step.Name);

                await step.ExecuteAsync(cancellationToken);
                executedSteps.Add(step);

                _logger.LogInformation(
                    "Saga step completed: {SagaId}/{StepName}",
                    saga.SagaId,
                    step.Name);
            }

            sw.Stop();

            _logger.LogInformation(
                "Saga {SagaId} completed successfully in {Duration}ms",
                saga.SagaId,
                sw.ElapsedMilliseconds);

            return new SagaResult(
                saga.SagaId,
                SagaStatus.Succeeded,
                executedSteps.Count,
                sw.Elapsed,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Saga {SagaId} failed at step {StepName}, starting compensation",
                saga.SagaId,
                executedSteps.Count > 0 ? executedSteps[^1].Name : "unknown");

            // Compensate in reverse order
            await CompensateAsync(saga.SagaId, executedSteps, cancellationToken);

            sw.Stop();

            return new SagaResult(
                saga.SagaId,
                SagaStatus.Compensated,
                executedSteps.Count,
                sw.Elapsed,
                ex.Message);
        }
    }

    private async ValueTask CompensateAsync(
        string sagaId,
        List<ISagaStep> executedSteps,
        CancellationToken cancellationToken)
    {
        // Compensate in reverse order
        for (int i = executedSteps.Count - 1; i >= 0; i--)
        {
            var step = executedSteps[i];

            try
            {
                _logger.LogInformation(
                    "Compensating saga step: {SagaId}/{StepName}",
                    sagaId,
                    step.Name);

                await step.CompensateAsync(cancellationToken);

                _logger.LogInformation(
                    "Saga step compensated: {SagaId}/{StepName}",
                    sagaId,
                    step.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to compensate saga step: {SagaId}/{StepName}",
                    sagaId,
                    step.Name);

                // Continue compensating other steps even if one fails
            }
        }

        _logger.LogInformation(
            "Saga {SagaId} compensation completed",
            sagaId);
    }
}

/// <summary>
/// Result of saga execution
/// </summary>
public sealed record SagaResult(
    string SagaId,
    SagaStatus Status,
    int StepsExecuted,
    TimeSpan Duration,
    string? ErrorMessage);

/// <summary>
/// Status of saga execution
/// </summary>
public enum SagaStatus
{
    /// <summary>
    /// Saga completed successfully
    /// </summary>
    Succeeded,

    /// <summary>
    /// Saga failed and was compensated
    /// </summary>
    Compensated,

    /// <summary>
    /// Saga failed and compensation also failed
    /// </summary>
    Failed
}

