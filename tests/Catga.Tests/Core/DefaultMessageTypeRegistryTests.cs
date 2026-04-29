using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

public class DefaultMessageTypeRegistryTests
{
    private sealed record TestMessage;

    [Fact]
    public void Register_ResolveByCanonicalName_ReturnsType()
    {
        var registry = new DefaultMessageTypeRegistry();

        registry.Register(typeof(TestMessage));

        registry.Resolve(typeof(TestMessage).FullName!).Should().Be(typeof(TestMessage));
        registry.GetTypeName(typeof(TestMessage)).Should().Be(typeof(TestMessage).FullName);
    }

    [Fact]
    public void Register_ResolveByLegacyAliases_ReturnsType()
    {
        var registry = new DefaultMessageTypeRegistry();

        registry.Register(typeof(TestMessage));

        registry.Resolve(typeof(TestMessage).Name).Should().Be(typeof(TestMessage));
        registry.Resolve(typeof(TestMessage).AssemblyQualifiedName!).Should().Be(typeof(TestMessage));
    }
}
