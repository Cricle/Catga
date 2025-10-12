using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Zero-allocation type name cache (no reflection after first access)</summary>
public static class TypeNameCache<T>
{
    private static string? _name;
    private static string? _fullName;

    /// <summary>Gets the type name (cached, no reflection after first call)</summary>
    public static string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _name ??= typeof(T).Name;
    }

    /// <summary>Gets the full type name (cached, no reflection after first call)</summary>
    public static string FullName
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _fullName ??= typeof(T).FullName ?? typeof(T).Name;
    }
}

