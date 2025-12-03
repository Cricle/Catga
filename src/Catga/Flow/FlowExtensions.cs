using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.Flow;

/// <summary>
/// Extension methods for flow-based orchestration.
/// </summary>
public static class FlowExtensions
{
    /// <summary>
    /// Begins a new flow context for orchestrating multiple commands with automatic compensation.
    /// </summary>
    /// <param name="mediator">The mediator instance</param>
    /// <param name="flowName">Name of the flow for tracing</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>A flow context that should be disposed with 'await using'</returns>
    /// <example>
    /// <code>
    /// await using var flow = mediator.BeginFlow("CreateOrder");
    /// var order = await flow.ExecuteAsync&lt;CreateOrderCommand, OrderCreated&gt;(cmd, ct);
    /// var stock = await flow.ExecuteAsync&lt;ReserveStockCommand, StockReserved&gt;(cmd, ct);
    /// flow.Commit(); // Success - no compensation on dispose
    /// </code>
    /// </example>
    public static FlowContext BeginFlow(
        this ICatgaMediator mediator,
        string flowName,
        long? correlationId = null,
        ILogger? logger = null)
    {
        return FlowContext.Begin(mediator, flowName, correlationId, logger);
    }

    /// <summary>
    /// Executes a command within the current flow context (if any).
    /// If in a flow, automatically records compensation for rollback.
    /// If not in a flow, executes normally without compensation tracking.
    /// </summary>
    /// <typeparam name="TCommand">Command type</typeparam>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <param name="mediator">The mediator instance</param>
    /// <param name="command">The command to execute</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The command result</returns>
    public static async ValueTask<CatgaResult<TResult>> ExecuteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCommand, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this ICatgaMediator mediator,
        TCommand command,
        CancellationToken ct = default)
        where TCommand : IRequest<TResult>
    {
        var flow = FlowContext.Current;

        if (flow != null)
        {
            // Execute within flow context - compensation is automatic
            return await flow.ExecuteAsync<TCommand, TResult>(command, ct);
        }

        // No flow context - execute normally
        return await mediator.SendAsync<TCommand, TResult>(command, ct);
    }

    /// <summary>
    /// Executes a command without response within the current flow context.
    /// </summary>
    public static async ValueTask<CatgaResult> ExecuteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCommand>(
        this ICatgaMediator mediator,
        TCommand command,
        CancellationToken ct = default)
        where TCommand : IRequest
    {
        var flow = FlowContext.Current;

        if (flow != null)
        {
            return await flow.ExecuteAsync(command, ct);
        }

        return await mediator.SendAsync(command, ct);
    }

    /// <summary>
    /// Executes a command and throws on failure.
    /// Useful for cleaner code when you want exceptions instead of Result pattern.
    /// </summary>
    public static async ValueTask<TResult> ExecuteOrThrowAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCommand, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this ICatgaMediator mediator,
        TCommand command,
        CancellationToken ct = default)
        where TCommand : IRequest<TResult>
    {
        var result = await mediator.ExecuteAsync<TCommand, TResult>(command, ct);

        if (!result.IsSuccess)
        {
            throw new FlowExecutionException(
                typeof(TCommand).Name,
                result.Error ?? "Unknown error",
                FlowContext.Current?.StepCount ?? 0);
        }

        return result.Value!;
    }

    /// <summary>
    /// Runs a flow with automatic context management.
    /// </summary>
    /// <typeparam name="TResult">The final result type</typeparam>
    /// <param name="mediator">The mediator instance</param>
    /// <param name="flowName">Name of the flow</param>
    /// <param name="flowAction">The flow execution logic</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The flow result</returns>
    /// <example>
    /// <code>
    /// var result = await mediator.RunFlowAsync("CreateOrder", async flow =>
    /// {
    ///     var order = await flow.ExecuteAsync&lt;CreateOrder, OrderCreated&gt;(cmd, ct);
    ///     var stock = await flow.ExecuteAsync&lt;ReserveStock, StockReserved&gt;(cmd, ct);
    ///     return new Order { Id = order.Value.OrderId };
    /// }, ct);
    /// </code>
    /// </example>
    public static async ValueTask<FlowResult<TResult>> RunFlowAsync<TResult>(
        this ICatgaMediator mediator,
        string flowName,
        Func<FlowContext, ValueTask<TResult>> flowAction,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        await using var flow = mediator.BeginFlow(flowName);

        try
        {
            var result = await flowAction(flow);
            flow.Commit();

            return FlowResult<TResult>.Success(result, DateTime.UtcNow - startTime);
        }
        catch (FlowExecutionException ex)
        {
            return FlowResult<TResult>.Failure(ex.Message, ex.FailedStep, DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            return FlowResult<TResult>.Failure(ex.Message, flow.StepCount, DateTime.UtcNow - startTime);
        }
    }

    /// <summary>
    /// Runs a flow using the simple async/await pattern.
    /// </summary>
    /// <example>
    /// <code>
    /// var order = await mediator.RunFlowAsync("CreateOrder", async () =>
    /// {
    ///     var order = await mediator.ExecuteOrThrowAsync&lt;CreateOrder, OrderCreated&gt;(cmd, ct);
    ///     var stock = await mediator.ExecuteOrThrowAsync&lt;ReserveStock, StockReserved&gt;(cmd, ct);
    ///     return new Order { Id = order.OrderId };
    /// }, ct);
    /// </code>
    /// </example>
    public static async ValueTask<FlowResult<TResult>> RunFlowAsync<TResult>(
        this ICatgaMediator mediator,
        string flowName,
        Func<ValueTask<TResult>> flowAction,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        await using var flow = mediator.BeginFlow(flowName);

        try
        {
            var result = await flowAction();
            flow.Commit();

            return FlowResult<TResult>.Success(result, DateTime.UtcNow - startTime);
        }
        catch (FlowExecutionException ex)
        {
            return FlowResult<TResult>.Failure(ex.Message, ex.FailedStep, DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            return FlowResult<TResult>.Failure(ex.Message, flow.StepCount, DateTime.UtcNow - startTime);
        }
    }
}

/// <summary>
/// Exception thrown when a flow step fails.
/// </summary>
public class FlowExecutionException : Exception
{
    /// <summary>
    /// The command type that failed.
    /// </summary>
    public string CommandType { get; }

    /// <summary>
    /// The step number that failed (1-based).
    /// </summary>
    public int FailedStep { get; }

    public FlowExecutionException(string commandType, string message, int failedStep)
        : base($"Flow step {failedStep} ({commandType}) failed: {message}")
    {
        CommandType = commandType;
        FailedStep = failedStep;
    }
}
