using System.Diagnostics.CodeAnalysis;

namespace Catga.Core;

/// <summary>Zero-allocation type name cache (thread-safe, no reflection after first access)</summary>
public static class TypeNameCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
{
    public static readonly string Name = typeof(T).Name;
    public static readonly string FullName = typeof(T).FullName ?? typeof(T).Name;
}