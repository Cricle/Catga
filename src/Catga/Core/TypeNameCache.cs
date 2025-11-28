using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Zero-allocation type name cache (no reflection after first access, thread-safe)</summary>
public static class TypeNameCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
{
    private static readonly string _name = typeof(T).Name;
    private static readonly string _fullName = typeof(T).FullName ?? typeof(T).Name;

    /// <summary>Gets the type name (cached, thread-safe, no reflection after first call)</summary>
    public static string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _name;
    }

    /// <summary>Gets the full type name (cached, thread-safe, no reflection after first call)</summary>
    public static string FullName
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _fullName;
    }
}

