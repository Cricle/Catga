using System.Collections.Concurrent;

namespace Catga.Core;

/// <summary>
/// Zero-allocation exception type name cache.
/// Caches exception type names to avoid repeated reflection calls.
/// </summary>
public static class ExceptionTypeCache
{
    private static readonly ConcurrentDictionary<Type, string> NameCache = new();
    private static readonly ConcurrentDictionary<Type, string> FullNameCache = new();

    /// <summary>
    /// Gets the cached type name for an exception.
    /// </summary>
    public static string GetTypeName(Exception ex)
    {
        var t = ex.GetType();
        return NameCache.GetOrAdd(t, static tp => tp.Name);
    }

    /// <summary>
    /// Gets the cached full type name for an exception.
    /// </summary>
    public static string GetFullTypeName(Exception ex)
    {
        var t = ex.GetType();
        return FullNameCache.GetOrAdd(t, static tp => tp.FullName ?? tp.Name);
    }
}
