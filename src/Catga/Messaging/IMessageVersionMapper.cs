using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Messaging;

/// <summary>
/// Maps message type names across versions and upgrades message content.
/// Enables rolling deployments where old and new message versions coexist.
/// </summary>
public interface IMessageVersionMapper
{
    /// <summary>
    /// Register a type alias: old type name → new type.
    /// Used when a message is renamed or moved between namespaces.
    /// </summary>
    IMessageVersionMapper AddAlias(string oldTypeName, Type newType);

    /// <summary>
    /// Register a content upgrader: old message → new message.
    /// Used when message fields change between versions.
    /// </summary>
    IMessageVersionMapper AddUpgrader<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOld,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TNew>(
        Func<TOld, TNew> upgrader)
        where TOld : IMessage
        where TNew : IMessage;

    /// <summary>
    /// Resolve the current type for a given type name (handles aliases).
    /// Returns null if no mapping exists (use original).
    /// </summary>
    Type? ResolveType(string typeName);

    /// <summary>
    /// Upgrade a message to its latest version.
    /// Returns the same message if no upgrader is registered.
    /// </summary>
    IMessage Upgrade(IMessage message);

    /// <summary>
    /// Get the canonical type name for a message type (for serialization).
    /// </summary>
    string GetTypeName(Type messageType);
}

/// <summary>
/// Marks a message type with its schema version.
/// Used for documentation and version tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class MessageVersionAttribute : Attribute
{
    public int Version { get; }
    public MessageVersionAttribute(int version) => Version = version;
}
