using System;

namespace Catga.Abstractions;

/// <summary>
/// Marks a property to be exported as a distributed tracing tag on Activity.
/// Source generator will emit a zero-allocation <see cref="IActivityTagProvider"/> implementation
/// for request/query types to set these tags at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TraceTagAttribute : Attribute
{
    /// <summary>
    /// Optional tag name. If not specified, defaults to "catga.req.{PropertyName}".
    /// </summary>
    public string? Name { get; }

    public TraceTagAttribute(string? name = null)
    {
        Name = name;
    }
}
