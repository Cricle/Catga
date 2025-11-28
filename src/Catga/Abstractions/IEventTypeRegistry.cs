using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

public interface IEventTypeRegistry
{
    void Register(string typeName, Type type);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    Type? Resolve(string typeName);
}
