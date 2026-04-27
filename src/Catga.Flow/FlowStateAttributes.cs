namespace Catga.Flow.Dsl;

/// <summary>
/// Marks a class for flow state source generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FlowStateAttribute : Attribute
{
}

/// <summary>
/// Marks a property to be excluded from flow state change tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FlowStateIgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a backing field for automatic property generation with change tracking.
/// Used within [FlowState] classes to define trackable fields.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class FlowStateFieldAttribute : Attribute
{
}
