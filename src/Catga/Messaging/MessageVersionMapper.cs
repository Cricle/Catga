using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Messaging;

/// <summary>
/// Default implementation of IMessageVersionMapper.
/// Thread-safe for concurrent reads after registration.
/// </summary>
public sealed class MessageVersionMapper : IMessageVersionMapper
{
    // typeName → current Type (for deserialization of old messages)
    private readonly Dictionary<string, Type> _aliases = new(StringComparer.OrdinalIgnoreCase);

    // sourceType → upgrade function
    private readonly Dictionary<Type, Func<IMessage, IMessage>> _upgraders = new();

    // Type → canonical name (reverse lookup)
    private readonly Dictionary<Type, string> _typeNames = new();

    public IMessageVersionMapper AddAlias(string oldTypeName, Type newType)
    {
        _aliases[oldTypeName] = newType;
        return this;
    }

    public IMessageVersionMapper AddUpgrader<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOld,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TNew>(
        Func<TOld, TNew> upgrader)
        where TOld : IMessage
        where TNew : IMessage
    {
        _upgraders[typeof(TOld)] = msg => upgrader((TOld)msg);
        return this;
    }

    public Type? ResolveType(string typeName)
        => _aliases.TryGetValue(typeName, out var t) ? t : null;

    public IMessage Upgrade(IMessage message)
    {
        var current = message;
        var maxIterations = 20;

        for (var i = 0; i < maxIterations; i++)
        {
            if (!_upgraders.TryGetValue(current.GetType(), out var upgrader))
                break;
            var upgraded = upgrader(current);
            if (upgraded.GetType() == current.GetType()) break; // no change
            current = upgraded;
        }

        return current;
    }

    public string GetTypeName(Type messageType)
    {
        if (_typeNames.TryGetValue(messageType, out var name)) return name;
        return TypeNameCache.GetName(messageType);
    }

    public void RegisterTypeName(Type messageType, string name)
        => _typeNames[messageType] = name;
}

/// <summary>
/// Fluent builder for MessageVersionMapper.
/// </summary>
public sealed class MessageVersionMapperBuilder
{
    private readonly MessageVersionMapper _mapper = new();

    /// <summary>Map old type name to new type (rename/move).</summary>
    public MessageVersionMapperBuilder MapType(string oldName, Type newType)
    {
        _mapper.AddAlias(oldName, newType);
        return this;
    }

    /// <summary>Register content upgrader from old to new version.</summary>
    public MessageVersionMapperBuilder Upgrade<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOld,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TNew>(
        Func<TOld, TNew> upgrader)
        where TOld : IMessage
        where TNew : IMessage
    {
        _mapper.AddUpgrader(upgrader);
        return this;
    }

    public IMessageVersionMapper Build() => _mapper;
}

/// <summary>
/// Simple type name cache for non-generic types.
/// </summary>
internal static class TypeNameCache
{
    public static string GetName(Type t) => t.FullName ?? t.Name;
}
