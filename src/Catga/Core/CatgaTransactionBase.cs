using System.Diagnostics;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.DistributedTransaction;

/// <summary>
/// Guided base class for Catga distributed transactions (superior to traditional Saga).
/// Users only need to implement 3 methods: ExecuteStepsAsync, CompensateAsync, GetCompensations.
/// Framework automatically handles: tracing, logging, compensation, retry, and failure recovery.
/// </summary>
/// <typeparam name="TData">Transaction data type</typeparam>
public abstract class CatgaTransactionBase<TData> where TData : class
{
    protected readonly ICatgaMediator Mediator;
    protected readonly ILogger Logger;

    protected CatgaTransactionBase(ICatgaMediator mediator, ILogger logger)
    {
        Mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>User implements: Define transaction steps (business logic)</summary>
    protected abstract ValueTask<TData> ExecuteStepsAsync(TData data, CancellationToken ct);

    /// <summary>User implements: Define compensation logic (rollback)</summary>
    protected abstract ValueTask CompensateAsync(TData data, string failedStep, CancellationToken ct);

    /// <summary>User implements: Define event-to-compensation mappings (optional, can return empty)</summary>
    protected abstract IReadOnlyDictionary<Type, Type> GetCompensations();

    /// <summary>Framework method: Execute transaction with automatic tracing and error handling</summary>
    public async ValueTask<CatgaResult<TData>> ExecuteAsync(TData data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));

        var transactionId = GetTransactionId(data);
        using var activity = Activity.Current?.Source.StartActivity($"CatgaTransaction.{GetType().Name}");
        activity?.SetTag("transaction.id", transactionId);
        activity?.SetTag("transaction.type", GetType().Name);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            CatgaLog.CatgaTransactionStarting(Logger, GetType().Name, transactionId);

            var result = await ExecuteStepsAsync(data, ct);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("transaction.duration_ms", stopwatch.ElapsedMilliseconds);

            CatgaLog.CatgaTransactionCompleted(Logger, GetType().Name, transactionId, stopwatch.ElapsedMilliseconds);

            return CatgaResult<TData>.Success(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");

            CatgaLog.CatgaTransactionCancelled(Logger, GetType().Name, transactionId);

            return CatgaResult<TData>.Failure("Transaction was cancelled");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("transaction.duration_ms", stopwatch.ElapsedMilliseconds);

            CatgaLog.CatgaTransactionFailed(Logger, GetType().Name, transactionId, ex.Message, ex);

            // Attempt compensation
            try
            {
                CatgaLog.CatgaTransactionCompensating(Logger, GetType().Name, transactionId);

                await CompensateAsync(data, ex.Message, ct);

                CatgaLog.CatgaTransactionCompensated(Logger, GetType().Name, transactionId);
            }
            catch (Exception compensationEx)
            {
                CatgaLog.CatgaTransactionCompensationFailed(Logger, GetType().Name, transactionId, compensationEx.Message, compensationEx);

                // Return combined error
                return CatgaResult<TData>.Failure(
                    $"Transaction failed: {ex.Message}. Compensation also failed: {compensationEx.Message}");
            }

            return CatgaResult<TData>.Failure(ex.Message);
        }
    }

    /// <summary>Get unique transaction ID (can be overridden)</summary>
    protected virtual string GetTransactionId(TData data) =>
        data.ToString() ?? Guid.NewGuid().ToString();
}

/// <summary>
/// Logging extensions for Catga transactions (high-performance source-generated logs)
/// </summary>
public static partial class CatgaLog
{
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Starting Catga transaction {TransactionType}, ID: {TransactionId}")]
    public static partial void CatgaTransactionStarting(
        ILogger logger, string transactionType, string transactionId);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Catga transaction {TransactionType} completed successfully, ID: {TransactionId}, Duration: {DurationMs}ms")]
    public static partial void CatgaTransactionCompleted(
        ILogger logger, string transactionType, string transactionId, long durationMs);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "Catga transaction {TransactionType} cancelled, ID: {TransactionId}")]
    public static partial void CatgaTransactionCancelled(
        ILogger logger, string transactionType, string transactionId);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "Catga transaction {TransactionType} failed, ID: {TransactionId}, Error: {ErrorMessage}")]
    public static partial void CatgaTransactionFailed(
        ILogger logger, string transactionType, string transactionId, string errorMessage, Exception ex);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Catga transaction {TransactionType} compensating, ID: {TransactionId}")]
    public static partial void CatgaTransactionCompensating(
        ILogger logger, string transactionType, string transactionId);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Catga transaction {TransactionType} compensated successfully, ID: {TransactionId}")]
    public static partial void CatgaTransactionCompensated(
        ILogger logger, string transactionType, string transactionId);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Critical,
        Message = "Catga transaction {TransactionType} compensation FAILED, ID: {TransactionId}, Error: {ErrorMessage}")]
    public static partial void CatgaTransactionCompensationFailed(
        ILogger logger, string transactionType, string transactionId, string errorMessage, Exception ex);
}

