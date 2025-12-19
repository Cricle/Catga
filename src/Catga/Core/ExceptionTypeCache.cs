using System.Collections.Concurrent;

namespace Catga.Core;

/// <summary>
/// Zero-allocation exception type name cache.
/// </summary>
public static class ExceptionTypeCache
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    public static string GetTypeName(Exception ex)
        => Cache.GetOrAdd(ex.GetType(), static t => t.Name);

    public static string GetFullTypeName(Exception ex)
        => Cache.GetOrAdd(ex.GetType(), static t => t.FullName ?? t.Name);
}
