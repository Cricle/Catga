using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Zero-allocation type name cache (no reflection after first access, thread-safe)</summary>
public static class TypeNameCache<T>
{
    // ✅ 线程安全：Lazy<T> 使用双检锁模式，保证线程安全和内存可见性
    private static readonly Lazy<string> _name = new(() => typeof(T).Name, LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<string> _fullName = new(() => typeof(T).FullName ?? typeof(T).Name, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Gets the type name (cached, thread-safe, no reflection after first call)</summary>
    public static string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _name.Value;
    }

    /// <summary>Gets the full type name (cached, thread-safe, no reflection after first call)</summary>
    public static string FullName
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _fullName.Value;
    }
}

/// <summary>Cache for exception type names (non-generic fallback for runtime types)</summary>
public static class ExceptionTypeCache
{
    private static readonly ConcurrentDictionary<Type, string> _nameCache = new();
    private static readonly ConcurrentDictionary<Type, string> _fullNameCache = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetTypeName(Exception exception)
    {
        var type = exception.GetType();
        return _nameCache.GetOrAdd(type, t => t.Name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetFullTypeName(Exception exception)
    {
        var type = exception.GetType();
        return _fullNameCache.GetOrAdd(type, t => t.FullName ?? t.Name);
    }
}

