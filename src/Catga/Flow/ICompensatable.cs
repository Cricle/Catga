using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Flow;

/// <summary>
/// Interface for commands that can create compensation commands.
/// Implemented automatically by Source Generator for commands with [Compensation] attribute.
/// </summary>
public interface ICompensatable
{
    /// <summary>
    /// The compensation command type.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    Type CompensationType { get; }

    /// <summary>
    /// Creates the compensation command from the execution result.
    /// </summary>
    /// <param name="result">The result of the original command execution</param>
    /// <returns>The compensation command to execute on rollback</returns>
    IRequest CreateCompensation(object result);
}

/// <summary>
/// Strongly-typed interface for commands with compensation.
/// </summary>
/// <typeparam name="TResult">The result type of the original command</typeparam>
/// <typeparam name="TCompensation">The compensation command type</typeparam>
public interface ICompensatable<TResult, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCompensation>
    : ICompensatable
    where TCompensation : IRequest
{
    /// <summary>
    /// Creates the compensation command from the execution result.
    /// </summary>
    /// <param name="result">The result of the original command execution</param>
    /// <returns>The compensation command to execute on rollback</returns>
    TCompensation CreateCompensation(TResult result);

    // Default implementation for non-generic interface
    IRequest ICompensatable.CreateCompensation(object result) => CreateCompensation((TResult)result);
    Type ICompensatable.CompensationType => typeof(TCompensation);
}

/// <summary>
/// Represents a recorded compensation action in the flow.
/// </summary>
public readonly struct CompensationRecord
{
    /// <summary>
    /// The step index (1-based).
    /// </summary>
    public int StepIndex { get; init; }

    /// <summary>
    /// The original command type name.
    /// </summary>
    public string CommandTypeName { get; init; }

    /// <summary>
    /// The compensation action to execute.
    /// </summary>
    public required Func<CancellationToken, Task> CompensationAction { get; init; }

    /// <summary>
    /// When the step was executed.
    /// </summary>
    public DateTime ExecutedAt { get; init; }
}
