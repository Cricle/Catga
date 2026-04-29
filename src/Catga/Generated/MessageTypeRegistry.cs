using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Generated;

/// <summary>
/// Registry for message types discovered by source generator.
/// Stores a canonical name plus compatibility aliases for runtime resolution.
/// </summary>
public static class MessageTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _typesByName = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, string> _namesByType = new();

    public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
    {
        var type = typeof(T);
        Register(type);
    }

    public static void Register([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var canonicalName = type.FullName ?? type.Name;
        _typesByName.TryAdd(canonicalName, type);
        _namesByType.TryAdd(type, canonicalName);

        if (!string.Equals(type.Name, canonicalName, StringComparison.Ordinal))
            _typesByName.TryAdd(type.Name, type);

        if (!string.IsNullOrWhiteSpace(type.AssemblyQualifiedName))
            _typesByName.TryAdd(type.AssemblyQualifiedName, type);
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public static Type? GetType(string typeName)
        => _typesByName.TryGetValue(typeName, out var type) ? type : null;

    public static string? GetName([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => _namesByType.TryGetValue(type, out var name) ? name : null;

    public static string? GetName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => GetName(typeof(T));

    public static IReadOnlyCollection<Type> GetAllTypes()
        => _typesByName.Values.ToArray();

    public static void Clear()
    {
        _typesByName.Clear();
        _namesByType.Clear();
    }
}
