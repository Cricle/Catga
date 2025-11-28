using System;
using System.Collections.Concurrent;

namespace Catga.Core;

public static class ExceptionTypeCache
{
    private static readonly ConcurrentDictionary<Type, string> NameCache = new();
    private static readonly ConcurrentDictionary<Type, string> FullNameCache = new();

    public static string GetTypeName(Exception ex)
    {
        var t = ex.GetType();
        return NameCache.GetOrAdd(t, static tp => tp.Name);
    }

    public static string GetFullTypeName(Exception ex)
    {
        var t = ex.GetType();
        return FullNameCache.GetOrAdd(t, static tp => tp.FullName ?? tp.Name);
    }
}
