using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

/// <summary>
/// Registry for runtime message type resolution across requests and events.
/// Used by outbox/scheduling pipelines to persist stable message type names
/// without depending on <see cref="Type.GetType(string)"/>.
/// </summary>
public interface IMessageTypeRegistry
{
    /// <summary>
    /// Register a message type under an explicit name.
    /// </summary>
    void Register(
        string typeName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Register a message type and its compatibility aliases.
    /// </summary>
    void Register(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Resolve a previously registered message type.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    Type? Resolve(string typeName);

    /// <summary>
    /// Get the canonical persisted name for a message type.
    /// </summary>
    string GetTypeName(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);
}
