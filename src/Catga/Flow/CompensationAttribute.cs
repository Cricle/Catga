using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow;

/// <summary>
/// Marks a command with its compensation command type.
/// When the flow fails, compensation commands are executed in reverse order.
/// </summary>
/// <example>
/// <code>
/// [Compensation(typeof(CancelOrderCommand))]
/// public record CreateOrderCommand(long UserId) : IRequest&lt;OrderCreated&gt;;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class CompensationAttribute : Attribute
{
    /// <summary>
    /// The compensation command type to execute on failure.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type CompensationType { get; }

    /// <summary>
    /// Property name mappings from result to compensation command.
    /// Format: "ResultProperty:CompensationProperty" or just "PropertyName" if same.
    /// If empty, auto-maps properties with matching names.
    /// </summary>
    public string[]? PropertyMappings { get; set; }

    /// <summary>
    /// Creates a compensation attribute.
    /// </summary>
    /// <param name="compensationType">The compensation command type</param>
    public CompensationAttribute(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type compensationType)
    {
        CompensationType = compensationType ?? throw new ArgumentNullException(nameof(compensationType));
    }
}

/// <summary>
/// Marks a method as a flow that automatically handles compensation on failure.
/// Source generator will wrap the method with flow context management.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class FlowAttribute : Attribute
{
    /// <summary>
    /// Flow name for tracing and logging.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether to persist flow state for recovery after restart.
    /// Default: false (in-memory only)
    /// </summary>
    public bool Persist { get; set; }

    /// <summary>
    /// Maximum retry attempts for the entire flow.
    /// Default: 0 (no retry)
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Timeout for the entire flow.
    /// Default: no timeout
    /// </summary>
    public int TimeoutSeconds { get; set; }

    public FlowAttribute() { }

    public FlowAttribute(string name)
    {
        Name = name;
    }
}
