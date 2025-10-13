using System.Diagnostics.CodeAnalysis;
using Catga.Messages;

namespace Catga.Transaction;

/// <summary>Transaction step - represents a single operation in the transaction</summary>
internal abstract class TransactionStep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext> 
    where TContext : class
{
    public abstract Task<StepResult> ExecuteAsync(
        TContext context,
        ICatgaMediator mediator,
        CancellationToken cancellationToken);

    public virtual Task<StepResult> CompensateAsync(
        TContext context,
        ICatgaMediator mediator,
        CancellationToken cancellationToken)
        => Task.FromResult(StepResult.Success());
}

/// <summary>Step execution result</summary>
internal readonly struct StepResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public Exception? Exception { get; init; }

    public static StepResult Success() => new() { IsSuccess = true };
    public static StepResult Failure(string error, Exception? exception = null)
        => new() { IsSuccess = false, Error = error, Exception = exception };
}

/// <summary>Command execution step</summary>
internal sealed class CommandStep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext, TCommand, TEvent> 
    : TransactionStep<TContext>
    where TContext : class
    where TCommand : ICommand
    where TEvent : IEvent
{
    private readonly Func<TContext, TCommand> _commandFactory;
    private readonly Func<TContext, TEvent, TContext> _onSuccess;
    private readonly Func<TContext, Exception, TContext>? _onFailure;
    private Func<TContext, ICommand>? _compensation;

    public CommandStep(
        Func<TContext, TCommand> commandFactory,
        Func<TContext, TEvent, TContext> onSuccess,
        Func<TContext, Exception, TContext>? onFailure = null)
    {
        _commandFactory = commandFactory;
        _onSuccess = onSuccess;
        _onFailure = onFailure;
    }

    public void SetCompensation(Func<TContext, ICommand> compensation)
        => _compensation = compensation;

    public override async Task<StepResult> ExecuteAsync(
        TContext context,
        ICatgaMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = _commandFactory(context);
            var result = await mediator.SendAsync(command, cancellationToken);

            if (!result.IsSuccess)
                return StepResult.Failure(result.Error ?? "Command failed", result.Exception);

            // Wait for success event (simulated - in real scenario, use event subscription)
            // TODO: Implement event subscription mechanism
            return StepResult.Success();
        }
        catch (Exception ex)
        {
            if (_onFailure != null)
                _onFailure(context, ex);
            return StepResult.Failure(ex.Message, ex);
        }
    }

    public override async Task<StepResult> CompensateAsync(
        TContext context,
        ICatgaMediator mediator,
        CancellationToken cancellationToken)
    {
        if (_compensation == null)
            return StepResult.Success();

        try
        {
            var compensationCommand = _compensation(context);
            var result = await mediator.SendAsync(compensationCommand, cancellationToken);
            return result.IsSuccess
                ? StepResult.Success()
                : StepResult.Failure(result.Error ?? "Compensation failed");
        }
        catch (Exception ex)
        {
            return StepResult.Failure(ex.Message, ex);
        }
    }
}

/// <summary>Fire-and-forget step</summary>
internal sealed class FireStep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext, TCommand> 
    : TransactionStep<TContext>
    where TContext : class
    where TCommand : ICommand
{
    private readonly Func<TContext, TCommand> _commandFactory;

    public FireStep(Func<TContext, TCommand> commandFactory)
        => _commandFactory = commandFactory;

    public override async Task<StepResult> ExecuteAsync(
        TContext context,
        ICatgaMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = _commandFactory(context);
            _ = mediator.SendAsync(command, cancellationToken); // Fire and forget
            return StepResult.Success();
        }
        catch (Exception ex)
        {
            return StepResult.Failure(ex.Message, ex);
        }
    }
}

/// <summary>Transaction builder implementation</summary>
internal sealed class TransactionBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext> 
    : ITransactionBuilder<TContext>
    where TContext : class
{
    private readonly List<TransactionStep<TContext>> _steps = new();
    private TransactionStep<TContext>? _lastStep;

    public ITransactionBuilder<TContext> Execute<TCommand, TEvent>(
        Func<TContext, TCommand> commandFactory,
        Func<TContext, TEvent, TContext> onSuccess,
        Func<TContext, Exception, TContext>? onFailure = null)
        where TCommand : ICommand
        where TEvent : IEvent
    {
        var step = new CommandStep<TContext, TCommand, TEvent>(commandFactory, onSuccess, onFailure);
        _steps.Add(step);
        _lastStep = step;
        return this;
    }

    public ITransactionBuilder<TContext> Fire<TCommand>(Func<TContext, TCommand> commandFactory)
        where TCommand : ICommand
    {
        var step = new FireStep<TContext, TCommand>(commandFactory);
        _steps.Add(step);
        _lastStep = step;
        return this;
    }

    public ITransactionBuilder<TContext> When(
        Func<TContext, bool> condition,
        Action<ITransactionBuilder<TContext>> trueBranch,
        Action<ITransactionBuilder<TContext>>? falseBranch = null)
    {
        // TODO: Implement conditional branching
        throw new NotImplementedException("Conditional branching not yet implemented");
    }

    public ITransactionBuilder<TContext> Parallel(
        params Action<ITransactionBuilder<TContext>>[] branches)
    {
        // TODO: Implement parallel execution
        throw new NotImplementedException("Parallel execution not yet implemented");
    }

    public ITransactionBuilder<TContext> CompensateWith<TCommand>(Func<TContext, TCommand> commandFactory)
        where TCommand : ICommand
    {
        if (_lastStep is CommandStep<TContext, ICommand, IEvent> commandStep)
        {
            commandStep.SetCompensation(ctx => commandFactory(ctx));
        }
        return this;
    }

    public List<TransactionStep<TContext>> Build() => _steps;
}

