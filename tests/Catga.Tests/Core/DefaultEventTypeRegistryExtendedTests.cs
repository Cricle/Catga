using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for DefaultEventTypeRegistry to improve branch coverage.
/// </summary>
public class DefaultEventTypeRegistryExtendedTests
{
    [Fact]
    public void Register_ShouldRegisterType()
    {
        var registry = new DefaultEventTypeRegistry();
        registry.Register("TestEvent", typeof(TestEvent));

        var type = registry.Resolve("TestEvent");
        type.Should().Be(typeof(TestEvent));
    }

    [Fact]
    public void Register_WithEmptyName_ShouldNotRegister()
    {
        var registry = new DefaultEventTypeRegistry();
        registry.Register("", typeof(TestEvent));

        var type = registry.Resolve("");
        type.Should().BeNull();
    }

    [Fact]
    public void Register_WithNullName_ShouldNotRegister()
    {
        var registry = new DefaultEventTypeRegistry();
        registry.Register(null!, typeof(TestEvent));

        var type = registry.Resolve(null!);
        type.Should().BeNull();
    }

    [Fact]
    public void Resolve_UnregisteredType_ShouldReturnNull()
    {
        var registry = new DefaultEventTypeRegistry();
        var type = registry.Resolve("NonExistentEvent");
        type.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithEmptyName_ShouldReturnNull()
    {
        var registry = new DefaultEventTypeRegistry();
        var type = registry.Resolve("");
        type.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithNullName_ShouldReturnNull()
    {
        var registry = new DefaultEventTypeRegistry();
        var type = registry.Resolve(null!);
        type.Should().BeNull();
    }

    [Fact]
    public void Register_SameNameTwice_ShouldOverwrite()
    {
        var registry = new DefaultEventTypeRegistry();
        registry.Register("TestEvent", typeof(TestEvent));
        registry.Register("TestEvent", typeof(AnotherTestEvent));

        var type = registry.Resolve("TestEvent");
        type.Should().Be(typeof(AnotherTestEvent));
    }

    [Fact]
    public void GetPreservedType_ShouldReturnType()
    {
        var registry = new DefaultEventTypeRegistry();
        var evt = new TestEvent();

        var type = registry.GetPreservedType(evt);
        type.Should().Be(typeof(TestEvent));
    }

    [Fact]
    public void GetPreservedType_ShouldCacheType()
    {
        var registry = new DefaultEventTypeRegistry();
        var evt = new TestEvent();

        var type1 = registry.GetPreservedType(evt);
        var type2 = registry.GetPreservedType(evt);

        type1.Should().Be(type2);
    }

    [Fact]
    public void GetPreservedType_WithRegisteredType_ShouldReturnRegistered()
    {
        var registry = new DefaultEventTypeRegistry();
        var evt = new TestEvent();
        var typeName = typeof(TestEvent).AssemblyQualifiedName!;

        registry.Register(typeName, typeof(TestEvent));

        var type = registry.GetPreservedType(evt);
        type.Should().Be(typeof(TestEvent));
    }

    [Fact]
    public void MultipleTypes_ShouldBeIndependent()
    {
        var registry = new DefaultEventTypeRegistry();
        registry.Register("Type1", typeof(TestEvent));
        registry.Register("Type2", typeof(AnotherTestEvent));

        var type1 = registry.Resolve("Type1");
        var type2 = registry.Resolve("Type2");

        type1.Should().Be(typeof(TestEvent));
        type2.Should().Be(typeof(AnotherTestEvent));
    }

    public record TestEvent : IEvent
    {
        public long MessageId { get; init; }
    }

    public record AnotherTestEvent : IEvent
    {
        public long MessageId { get; init; }
    }
}
