using System.Diagnostics.CodeAnalysis;

namespace Catga.Core;

/// <summary>Zero-allocation type name cache (no reflection after first access, thread-safe)</summary>
public static class TypeNameCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
{
    /// <summary>Gets the type name (cached, thread-safe, no reflection after first call)</summary>
    public static readonly string Name = typeof(T).Name;

    /// <summary>Gets the full type name (cached, thread-safe, no reflection after first call)</summary>
    public static readonly string FullName = typeof(T).FullName ?? typeof(T).Name;
}

