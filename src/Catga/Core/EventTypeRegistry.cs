using System.Collections.Concurrent;

namespace Catga.Generated;

/// <summary>
/// Registry for event types discovered by source generator.
/// Used for event deserialization and type resolution.
/// </summary>
public static class EventTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _typesByName = new();
    private static readonly ConcurrentDictionary<Type, string> _namesByType = new();

    /// <summary>
    /// Register an event type. Called by source generator module initializer.
    /// </summary>
    public static void Register<T>()
    {
        var type = typeof(T);
        var name = type.FullName ?? type.Name;
        _typesByName.TryAdd(name, type);
        _namesByType.TryAdd(type, name);
    }

    /// <summary>
    /// Get event type by name.
    /// </summary>
    public static Type? GetType(string typeName)
        => _typesByName.TryGetValue(typeName, out var type) ? type : null;

    /// <summary>
    /// Get event name by type.
    /// </summary>
    public static string? GetName(Type type)
        => _namesByType.TryGetValue(type, out var name) ? name : null;

    /// <summary>
    /// Get event name by type.
    /// </summary>
    public static string? GetName<T>()
        => GetName(typeof(T));

    /// <summary>
    /// Check if type is registered.
    /// </summary>
    public static bool IsRegistered<T>()
        => _namesByType.ContainsKey(typeof(T));

    /// <summary>
    /// Check if type name is registered.
    /// </summary>
    public static bool IsRegistered(string typeName)
        => _typesByName.ContainsKey(typeName);

    /// <summary>
    /// Get all registered event types.
    /// </summary>
    public static IReadOnlyCollection<Type> GetAllTypes()
        => _typesByName.Values.ToArray();

    /// <summary>
    /// Get all registered event type names.
    /// </summary>
    public static IReadOnlyCollection<string> GetAllNames()
        => _typesByName.Keys.ToArray();

    /// <summary>
    /// Clear all registrations (for testing).
    /// </summary>
    public static void Clear()
    {
        _typesByName.Clear();
        _namesByType.Clear();
    }
}
