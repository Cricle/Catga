using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

public interface IEventTypeRegistry
{
    void Register(string typeName, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    Type? Resolve(string typeName);

    /// <summary>
    /// Get preserved type for an event. Returns type with AOT annotations.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    Type GetPreservedType(IEvent @event);
}
