using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Core;

public sealed class DefaultEventTypeRegistry : IEventTypeRegistry
{
    // ========== Fields ==========

    private readonly ConcurrentDictionary<string, Type> _map = new(StringComparer.Ordinal);

    // ========== Public API ==========

    public void Register(string typeName, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
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

    [UnconditionalSuppressMessage("AOT", "IL2073", Justification = "Event types are registered at runtime via IEventTypeRegistry")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type GetPreservedType(IEvent @event)
    {
        var runtimeType = @event.GetType();
        var typeName = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName!;

        if (_map.TryGetValue(typeName, out var registeredType))
            return registeredType;

        _map[typeName] = runtimeType;
        return runtimeType;
    }
}
