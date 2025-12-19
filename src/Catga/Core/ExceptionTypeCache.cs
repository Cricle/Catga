using System.Collections.Concurrent;

namespace Catga.Core;

/// <summary>
/// Zero-allocation exception type name cache.
/// </summary>
public static class ExceptionTypeCache
{
    private static readonly ConcurrentDictionary<Type, string> NameCache = new();
    private static readonly ConcurrentDictionary<Type, string> FullNameCache = new();

    public static string GetTypeName(Exception ex)
        => NameCache.GetOrAdd(ex.GetType(), static t => t.Name);

    public static string GetFullTypeName(Exception ex)
        => FullNameCache.GetOrAdd(ex.GetType(), static t => t.FullName ?? t.Name);
}
