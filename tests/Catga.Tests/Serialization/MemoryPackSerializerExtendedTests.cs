using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Xunit;

namespace Catga.Tests.Serialization;

/// <summary>
/// Extended tests for MemoryPackMessageSerializer
/// </summary>
public sealed partial class MemoryPackSerializerExtendedTests
{
    private readonly MemoryPackMessageSerializer _serializer = new();

    [Fact]
    public void Serialize_SimpleRecord_ShouldWork()
    {
        // Arrange
        var message = new SimpleMessage { Id = 1, Name = "Test" };

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_SimpleRecord_ShouldWork()
    {
        // Arrange
        var original = new SimpleMessage { Id = 42, Name = "Hello" };
        var bytes = _serializer.Serialize(original);

        // Act
        var result = _serializer.Deserialize<SimpleMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Name.Should().Be("Hello");
    }

    [Fact]
    public void Serialize_WithType_ShouldWork()
    {
        // Arrange
        var message = new SimpleMessage { Id = 1, Name = "Test" };

        // Act
        var bytes = _serializer.Serialize(message, typeof(SimpleMessage));

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_WithType_ShouldWork()
    {
        // Arrange
        var original = new SimpleMessage { Id = 99, Name = "World" };
        var bytes = _serializer.Serialize(original, typeof(SimpleMessage));

        // Act
        var result = _serializer.Deserialize(bytes, typeof(SimpleMessage));

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<SimpleMessage>();
        ((SimpleMessage)result!).Id.Should().Be(99);
    }

    [Fact]
    public void Serialize_NestedObject_ShouldWork()
    {
        // Arrange
        var message = new NestedMessage
        {
            Id = 1,
            Inner = new InnerMessage { Value = "Nested" }
        };

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_NestedObject_ShouldWork()
    {
        // Arrange
        var original = new NestedMessage
        {
            Id = 5,
            Inner = new InnerMessage { Value = "Deep" }
        };
        var bytes = _serializer.Serialize(original);

        // Act
        var result = _serializer.Deserialize<NestedMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.Inner.Should().NotBeNull();
        result.Inner!.Value.Should().Be("Deep");
    }

    [Fact]
    public void Serialize_Collection_ShouldWork()
    {
        // Arrange
        var message = new CollectionMessage
        {
            Items = new List<string> { "a", "b", "c" }
        };

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_Collection_ShouldWork()
    {
        // Arrange
        var original = new CollectionMessage
        {
            Items = new List<string> { "x", "y", "z" }
        };
        var bytes = _serializer.Serialize(original);

        // Act
        var result = _serializer.Deserialize<CollectionMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
        result.Items.Should().Contain("x");
    }

    [Fact]
    public void Serialize_Dictionary_ShouldWork()
    {
        // Arrange
        var message = new DictionaryMessage
        {
            Data = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 }
        };

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_Dictionary_ShouldWork()
    {
        // Arrange
        var original = new DictionaryMessage
        {
            Data = new Dictionary<string, int> { ["a"] = 10, ["b"] = 20 }
        };
        var bytes = _serializer.Serialize(original);

        // Act
        var result = _serializer.Deserialize<DictionaryMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Data.Should().HaveCount(2);
        result.Data["a"].Should().Be(10);
    }

    [Fact]
    public void Serialize_LargeObject_ShouldWork()
    {
        // Arrange
        var message = new LargeMessage
        {
            Data = new string('x', 100000)
        };

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_LargeObject_ShouldWork()
    {
        // Arrange
        var largeData = new string('y', 50000);
        var original = new LargeMessage { Data = largeData };
        var bytes = _serializer.Serialize(original);

        // Act
        var result = _serializer.Deserialize<LargeMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Data.Should().HaveLength(50000);
    }

    [Fact]
    public void RoundTrip_MultipleTypes_ShouldWork()
    {
        // Arrange
        var messages = new object[]
        {
            new SimpleMessage { Id = 1, Name = "A" },
            new NestedMessage { Id = 2, Inner = new InnerMessage { Value = "B" } },
            new CollectionMessage { Items = new List<string> { "C" } }
        };

        // Act & Assert
        foreach (var msg in messages)
        {
            var bytes = _serializer.Serialize(msg, msg.GetType());
            var result = _serializer.Deserialize(bytes, msg.GetType());
            result.Should().NotBeNull();
        }
    }

    #region Test Types

    [MemoryPackable]
    private partial record SimpleMessage
    {
        public int Id { get; init; }
        public string? Name { get; init; }
    }

    [MemoryPackable]
    private partial record NestedMessage
    {
        public int Id { get; init; }
        public InnerMessage? Inner { get; init; }
    }

    [MemoryPackable]
    private partial record InnerMessage
    {
        public string? Value { get; init; }
    }

    [MemoryPackable]
    private partial record CollectionMessage
    {
        public List<string>? Items { get; init; }
    }

    [MemoryPackable]
    private partial record DictionaryMessage
    {
        public Dictionary<string, int>? Data { get; init; }
    }

    [MemoryPackable]
    private partial record LargeMessage
    {
        public string? Data { get; init; }
    }

    #endregion
}
