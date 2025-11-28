using System;

namespace Catga.Abstractions;

/// <summary>
/// Type-level tracing configuration. When applied to a partial type, the source generator
/// emits an IActivityTagProvider implementation that writes tags for selected properties.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
public sealed class TraceTagsAttribute : Attribute
{
    /// <summary>
    /// Optional tag prefix. If not set, defaults to "catga.req." for IRequest types, otherwise "catga.res.".
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// If true (default), include all public instance properties unless excluded.
    /// If false, only properties explicitly listed in Include are considered.
    /// </summary>
    public bool AllPublic { get; set; } = true;

    /// <summary>
    /// Properties to include by name. Takes effect when AllPublic=false, or to force include.
    /// </summary>
    public string[]? Include { get; set; }

    /// <summary>
    /// Properties to exclude by name when AllPublic=true.
    /// </summary>
    public string[]? Exclude { get; set; }

    public TraceTagsAttribute() { }
    public TraceTagsAttribute(string? prefix) => Prefix = prefix;
}
