using Catga.Serialization;
using Catga.Serialization.Json;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;

namespace Catga.Tests.Integration;

/// <summary>
/// Integration tests for message serialization with real serializers
/// </summary>
[Trait("Category", "Integration")]
public class SerializationIntegrationTests
{
    [Fact]
    public void MemoryPack_Should_Serialize_And_Deserialize_Complex_Message()
    {
        // Arrange
        var serializer = new MemoryPackMessageSerializer();
        var message = new ComplexMessage(
            "test-id",
            "Test Message",
            42,
            new[] { "item1", "item2", "item3" }.ToList(),
            new NestedData("nested-value", 3.14)
        );

        // Act
        var serialized = serializer.Serialize(message);
        var deserialized = serializer.Deserialize<ComplexMessage>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(message.Id);
        deserialized.Name.Should().Be(message.Name);
        deserialized.Count.Should().Be(message.Count);
        deserialized.Items.Should().BeEquivalentTo(message.Items);
        deserialized.Nested.Should().NotBeNull();
        deserialized.Nested.Value.Should().Be(message.Nested.Value);
        deserialized.Nested.Score.Should().Be(message.Nested.Score);
    }

    [Fact]
    public void Json_Should_Serialize_And_Deserialize_Complex_Message()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new ComplexMessage(
            "test-id",
            "Test Message",
            42,
            new[] { "item1", "item2", "item3" }.ToList(),
            new NestedData("nested-value", 3.14)
        );

        // Act
        var serialized = serializer.Serialize(message);
        var deserialized = serializer.Deserialize<ComplexMessage>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(message.Id);
        deserialized.Name.Should().Be(message.Name);
        deserialized.Count.Should().Be(message.Count);
        deserialized.Items.Should().BeEquivalentTo(message.Items);
        deserialized.Nested.Should().NotBeNull();
        deserialized.Nested.Value.Should().Be(message.Nested.Value);
        deserialized.Nested.Score.Should().Be(message.Nested.Score);
    }

    [Fact]
    public void MemoryPack_Should_Handle_Empty_Collections()
    {
        // Arrange
        var serializer = new MemoryPackMessageSerializer();
        var message = new ComplexMessage(
            "test-id",
            "Test",
            0,
            new List<string>(),
            new NestedData("value", 0)
        );

        // Act
        var serialized = serializer.Serialize(message);
        var deserialized = serializer.Deserialize<ComplexMessage>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().BeEmpty();
    }

    [Fact]
    public void MemoryPack_Should_Handle_Large_Messages()
    {
        // Arrange
        var serializer = new MemoryPackMessageSerializer();
        var largeList = Enumerable.Range(0, 10000).Select(i => $"item-{i}").ToList();
        var message = new ComplexMessage(
            "large-test",
            "Large Message",
            10000,
            largeList,
            new NestedData("nested", 1.0)
        );

        // Act
        var serialized = serializer.Serialize(message);
        var deserialized = serializer.Deserialize<ComplexMessage>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(10000);
        deserialized.Items.Should().BeEquivalentTo(largeList);
    }

    [Fact]
    public void Integration_MemoryPack_And_Json_Should_Produce_Different_Formats()
    {
        // Arrange
        var memoryPackSerializer = new MemoryPackMessageSerializer();
        var jsonSerializer = new JsonMessageSerializer();
        var message = new ComplexMessage(
            "test-id",
            "Test",
            42,
            new List<string> { "item1" },
            new NestedData("value", 1.0)
        );

        // Act
        var memoryPackBytes = memoryPackSerializer.Serialize(message);
        var jsonBytes = jsonSerializer.Serialize(message);

        // Assert - Different formats
        memoryPackBytes.Length.Should().NotBe(jsonBytes.Length);

        // But both should deserialize correctly
        var fromMemoryPack = memoryPackSerializer.Deserialize<ComplexMessage>(memoryPackBytes);
        var fromJson = jsonSerializer.Deserialize<ComplexMessage>(jsonBytes);

        fromMemoryPack.Id.Should().Be(message.Id);
        fromJson.Id.Should().Be(message.Id);
    }

    [Fact]
    public void MemoryPack_Should_Be_More_Compact_Than_Json()
    {
        // Arrange
        var memoryPackSerializer = new MemoryPackMessageSerializer();
        var jsonSerializer = new JsonMessageSerializer();
        var message = new ComplexMessage(
            "test-id-with-long-string",
            "Test Message with Long Name",
            42,
            Enumerable.Range(0, 100).Select(i => $"item-{i}").ToList(),
            new NestedData("nested-value-with-long-string", 3.14159265359)
        );

        // Act
        var memoryPackBytes = memoryPackSerializer.Serialize(message);
        var jsonBytes = jsonSerializer.Serialize(message);

        // Assert - MemoryPack should be more compact
        memoryPackBytes.Length.Should().BeLessThan(jsonBytes.Length);
    }
}

#region Test Messages

[MemoryPackable]
public partial record ComplexMessage(
    string Id,
    string Name,
    int Count,
    List<string> Items,
    NestedData Nested
);

[MemoryPackable]
public partial record NestedData(
    string Value,
    double Score
);

#endregion

