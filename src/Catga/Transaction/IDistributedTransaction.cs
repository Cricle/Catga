using System.Diagnostics.CodeAnalysis;
using Catga.Messages;

namespace Catga.Transaction;

/// <summary>Catga Distributed Transaction - declarative, event-driven, auto-compensating</summary>
public interface IDistributedTransaction<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext> 
    where TContext : class
{
    /// <summary>Transaction ID (for tracking and idempotency)</summary>
    string TransactionId { get; }

    /// <summary>Transaction name (for logging)</summary>
    string Name { get; }

    /// <summary>Define transaction flow using fluent API</summary>
    ITransactionBuilder<TContext> Define(ITransactionBuilder<TContext> builder);
}

/// <summary>Transaction builder - fluent API for defining transaction steps</summary>
public interface ITransactionBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext> 
    where TContext : class
{
    /// <summary>Execute a command and wait for completion event</summary>
    ITransactionBuilder<TContext> Execute<TCommand, TEvent>(
        Func<TContext, TCommand> commandFactory,
        Func<TContext, TEvent, TContext> onSuccess,
        Func<TContext, Exception, TContext>? onFailure = null)
        where TCommand : ICommand
        where TEvent : IEvent;

    /// <summary>Execute a command without waiting (fire-and-forget)</summary>
    ITransactionBuilder<TContext> Fire<TCommand>(Func<TContext, TCommand> commandFactory)
        where TCommand : ICommand;

    /// <summary>Conditional branching</summary>
    ITransactionBuilder<TContext> When(
        Func<TContext, bool> condition,
        Action<ITransactionBuilder<TContext>> trueBranch,
        Action<ITransactionBuilder<TContext>>? falseBranch = null);

    /// <summary>Parallel execution</summary>
    ITransactionBuilder<TContext> Parallel(
        params Action<ITransactionBuilder<TContext>>[] branches);

    /// <summary>Define compensation for previous step</summary>
    ITransactionBuilder<TContext> CompensateWith<TCommand>(Func<TContext, TCommand> commandFactory)
        where TCommand : ICommand;
}

