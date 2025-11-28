using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Core;

public sealed class DefaultEventTypeRegistry : IEventTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _map = new(StringComparer.Ordinal);

    public void Register(string typeName, Type type)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        _map[typeName] = type;
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? Resolve(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        return _map.TryGetValue(typeName, out var t) ? t : null;
    }
}
