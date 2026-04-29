using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Core;

/// <summary>
/// Default runtime message type registry with generated-registry and legacy fallbacks.
/// </summary>
public sealed class DefaultMessageTypeRegistry : IMessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _typesByName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _namesByType = new();

    public void Register(
        string typeName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(type);

        _typesByName[typeName] = type;
        CacheCanonicalName(type);
        RegisterCompatibilityAliases(type);
    }

    public void Register(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        CacheCanonicalName(type);
        RegisterCompatibilityAliases(type);
    }

    [UnconditionalSuppressMessage("AOT", "IL2057", Justification = "Message types are preserved via registration or generated registry before runtime fallback.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? Resolve(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        if (_typesByName.TryGetValue(typeName, out var registered))
            return registered;

        var generated = Catga.Generated.MessageTypeRegistry.GetType(typeName);
        if (generated is not null)
        {
            Register(generated);
            _typesByName[typeName] = generated;
            return generated;
        }

        var runtime = Type.GetType(typeName, throwOnError: false);
        if (runtime is not null)
        {
            Register(runtime);
            _typesByName[typeName] = runtime;
            return runtime;
        }

        return null;
    }

    public string GetTypeName(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_namesByType.TryGetValue(type, out var cached))
            return cached;

        var generated = Catga.Generated.MessageTypeRegistry.GetName(type);
        if (!string.IsNullOrWhiteSpace(generated))
        {
            _namesByType[type] = generated!;
            _typesByName.TryAdd(generated!, type);
            RegisterCompatibilityAliases(type);
            return generated!;
        }

        Register(type);
        return _namesByType[type];
    }

    private void CacheCanonicalName(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var canonical = type.FullName ?? type.Name;
        _namesByType[type] = canonical;
        _typesByName[canonical] = type;
    }

    private void RegisterCompatibilityAliases(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var simpleName = type.Name;
        if (!string.IsNullOrWhiteSpace(simpleName))
            _typesByName.TryAdd(simpleName, type);

        var fullName = type.FullName;
        if (!string.IsNullOrWhiteSpace(fullName))
            _typesByName.TryAdd(fullName, type);

        var assemblyQualifiedName = type.AssemblyQualifiedName;
        if (!string.IsNullOrWhiteSpace(assemblyQualifiedName))
            _typesByName.TryAdd(assemblyQualifiedName, type);
    }
}
