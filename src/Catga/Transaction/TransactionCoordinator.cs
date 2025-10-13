using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Observability;
using Microsoft.Extensions.Logging;

namespace Catga.Transaction;

/// <summary>Catga Transaction Coordinator - event-driven, auto-compensating, high-performance</summary>
public sealed class TransactionCoordinator : ITransactionCoordinator
{
    private readonly ICatgaMediator _mediator;
    private readonly ITransactionStore _store;
    private readonly ILogger<TransactionCoordinator> _logger;

    public TransactionCoordinator(
        ICatgaMediator mediator,
        ITransactionStore store,
        ILogger<TransactionCoordinator> logger)
    {
        _mediator = mediator;
        _store = store;
        _logger = logger;
    }

    public async Task<TransactionResult> StartAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext>(
        IDistributedTransaction<TContext> transaction,
        TContext context,
        TransactionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TContext : class
    {
        options ??= new TransactionOptions();

        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Transaction.Execute", ActivityKind.Internal);
        activity?.SetTag("transaction.id", transaction.TransactionId);
        activity?.SetTag("transaction.name", transaction.Name);

        var sw = Stopwatch.StartNew();
        CatgaLog.TransactionStarted(_logger, transaction.TransactionId, transaction.Name);

        // Build transaction steps
        var builder = new TransactionBuilder<TContext>();
        transaction.Define(builder);
        var steps = builder.Build();

        // Initialize snapshot
        var snapshot = new TransactionSnapshot
        {
            TransactionId = transaction.TransactionId,
            TransactionName = transaction.Name,
            Status = TransactionStatus.Running,
            StartedAt = DateTime.UtcNow,
            CurrentStep = 0,
            TotalSteps = steps.Count
        };

        await _store.SaveSnapshotAsync(snapshot, cancellationToken);

        // Create timeout cancellation
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            // Execute steps sequentially
            for (int i = 0; i < steps.Count; i++)
            {
                if (timeoutCts.Token.IsCancellationRequested)
                {
                    CatgaLog.TransactionTimedOut(_logger, transaction.TransactionId, i, options.Timeout.TotalSeconds);
                    snapshot = snapshot with { Status = TransactionStatus.TimedOut, CompletedAt = DateTime.UtcNow };
                    await _store.SaveSnapshotAsync(snapshot, cancellationToken);
                    return TransactionResult.TimedOut(snapshot);
                }

                var step = steps[i];
                snapshot = snapshot with { CurrentStep = i };
                await _store.SaveSnapshotAsync(snapshot, cancellationToken);

                CatgaLog.TransactionStepExecuting(_logger, transaction.TransactionId, i, steps.Count);

                // Execute with retry
                var result = await ExecuteWithRetryAsync(
                    () => step.ExecuteAsync(context, _mediator, timeoutCts.Token),
                    options.MaxRetries,
                    options.RetryDelay,
                    timeoutCts.Token);

                if (!result.IsSuccess)
                {
                    CatgaLog.TransactionStepFailed(_logger, transaction.TransactionId, i, result.Error ?? "Unknown");

                    // Record failure event
                    if (options.EnableEventSourcing)
                    {
                        var failureEvent = new TransactionEvent
                        {
                            EventType = "StepFailed",
                            Timestamp = DateTime.UtcNow,
                            Data = result.Error ?? "Unknown",
                            StepIndex = i
                        };
                        await _store.AppendEventAsync(transaction.TransactionId, failureEvent, cancellationToken);
                    }

                    // Auto-compensate if enabled
                    if (options.AutoCompensate)
                    {
                        return await CompensateAsync(transaction.TransactionId, steps, context, i - 1, snapshot, cancellationToken);
                    }

                    snapshot = snapshot with
                    {
                        Status = TransactionStatus.Failed,
                        Error = result.Error,
                        CompletedAt = DateTime.UtcNow
                    };
                    await _store.SaveSnapshotAsync(snapshot, cancellationToken);
                    return TransactionResult.Failure(result.Error ?? "Unknown", snapshot, result.Exception);
                }

                CatgaLog.TransactionStepCompleted(_logger, transaction.TransactionId, i);

                // Record success event
                if (options.EnableEventSourcing)
                {
                    var successEvent = new TransactionEvent
                    {
                        EventType = "StepCompleted",
                        Timestamp = DateTime.UtcNow,
                        Data = "Success",
                        StepIndex = i
                    };
                    await _store.AppendEventAsync(transaction.TransactionId, successEvent, cancellationToken);
                }
            }

            // Success
            snapshot = snapshot with
            {
                Status = TransactionStatus.Completed,
                CompletedAt = DateTime.UtcNow
            };
            await _store.SaveSnapshotAsync(snapshot, cancellationToken);

            sw.Stop();
            CatgaLog.TransactionCompleted(_logger, transaction.TransactionId, sw.Elapsed.TotalMilliseconds);

            activity?.SetTag("transaction.status", "completed");
            activity?.SetTag("transaction.duration_ms", sw.Elapsed.TotalMilliseconds);

            return TransactionResult.Success(snapshot);
        }
        catch (Exception ex)
        {
            sw.Stop();
            CatgaLog.TransactionFailed(_logger, ex, transaction.TransactionId, ex.Message);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("exception.type", ex.GetType().FullName);

            snapshot = snapshot with
            {
                Status = TransactionStatus.Failed,
                Error = ex.Message,
                CompletedAt = DateTime.UtcNow
            };
            await _store.SaveSnapshotAsync(snapshot, cancellationToken);

            return TransactionResult.Failure(ex.Message, snapshot, ex);
        }
    }

    public Task<TransactionSnapshot?> GetSnapshotAsync(string transactionId, CancellationToken cancellationToken = default)
        => _store.LoadSnapshotAsync(transactionId, cancellationToken);

    public Task<IReadOnlyList<TransactionSnapshot>> GetIncompleteTransactionsAsync(CancellationToken cancellationToken = default)
        => _store.GetIncompleteAsync(cancellationToken);

    private async Task<TransactionResult> CompensateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext>(
        string transactionId,
        List<TransactionStep<TContext>> steps,
        TContext context,
        int lastCompletedStepIndex,
        TransactionSnapshot snapshot,
        CancellationToken cancellationToken)
        where TContext : class
    {
        snapshot = snapshot with { Status = TransactionStatus.Compensating };
        await _store.SaveSnapshotAsync(snapshot, cancellationToken);

        CatgaLog.TransactionCompensating(_logger, transactionId, lastCompletedStepIndex + 1);

        // Compensate in reverse order
        for (int i = lastCompletedStepIndex; i >= 0; i--)
        {
            var step = steps[i];
            CatgaLog.TransactionStepCompensating(_logger, transactionId, i);

            try
            {
                var result = await step.CompensateAsync(context, _mediator, cancellationToken);

                if (!result.IsSuccess)
                {
                    CatgaLog.TransactionStepCompensationFailed(_logger, transactionId, i, result.Error ?? "Unknown");
                }
                else
                {
                    CatgaLog.TransactionStepCompensated(_logger, transactionId, i);
                }

                // Record compensation event
                var compensationEvent = new TransactionEvent
                {
                    EventType = result.IsSuccess ? "StepCompensated" : "CompensationFailed",
                    Timestamp = DateTime.UtcNow,
                    Data = result.Error ?? "Success",
                    StepIndex = i
                };
                await _store.AppendEventAsync(transactionId, compensationEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                CatgaLog.TransactionStepCompensationFailed(_logger, transactionId, i, ex.Message);
            }
        }

        snapshot = snapshot with
        {
            Status = TransactionStatus.Compensated,
            CompletedAt = DateTime.UtcNow
        };
        await _store.SaveSnapshotAsync(snapshot, cancellationToken);

        CatgaLog.TransactionCompensated(_logger, transactionId);

        return TransactionResult.Compensated(snapshot);
    }

    private static async Task<StepResult> ExecuteWithRetryAsync(
        Func<Task<StepResult>> action,
        int maxRetries,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                return StepResult.Failure("Operation cancelled");

            var result = await action();
            if (result.IsSuccess || attempt == maxRetries)
                return result;

            await Task.Delay(retryDelay * (attempt + 1), cancellationToken); // Exponential backoff
        }

        return StepResult.Failure("Max retries exceeded");
    }
}

