using Catga.Abstractions;
using Catga.Core;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Xunit;

namespace Catga.Tests.Serialization;

/// <summary>
/// Coverage tests for serialization
/// </summary>
public sealed partial class SerializationCoverageTests
{
    private readonly MemoryPackMessageSerializer _serializer = new();

    [Fact]
    public void Serialize_SimpleObject_ShouldSucceed()
    {
        // Arrange
        var obj = new SimpleMessage { Id = 1, Name = "Test" };

        // Act
        var bytes = _serializer.Serialize(obj, typeof(SimpleMessage));

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_SimpleObject_ShouldSucceed()
    {
        // Arrange
        var original = new SimpleMessage { Id = 42, Name = "Hello" };
        var bytes = _serializer.Serialize(original, typeof(SimpleMessage));

        // Act
        var result = _serializer.Deserialize<SimpleMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Name.Should().Be("Hello");
    }

    [Fact]
    public void Serialize_ComplexObject_ShouldSucceed()
    {
        // Arrange
        var obj = new ComplexMessage
        {
            Id = 1,
            Items = new List<string> { "a", "b", "c" },
            Nested = new NestedData { Value = 100 }
        };

        // Act
        var bytes = _serializer.Serialize(obj, typeof(ComplexMessage));

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_ComplexObject_ShouldPreserveData()
    {
        // Arrange
        var original = new ComplexMessage
        {
            Id = 99,
            Items = new List<string> { "x", "y", "z" },
            Nested = new NestedData { Value = 500 }
        };
        var bytes = _serializer.Serialize(original, typeof(ComplexMessage));

        // Act
        var result = _serializer.Deserialize<ComplexMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(99);
        result.Items.Should().BeEquivalentTo(new[] { "x", "y", "z" });
        result.Nested!.Value.Should().Be(500);
    }

    [Fact]
    public void Serialize_NullObject_ShouldThrow()
    {
        // Act
        var act = () => _serializer.Serialize(null!, typeof(SimpleMessage));

        // Assert - MemoryPack throws on null
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Deserialize_EmptyBytes_ShouldThrow()
    {
        // Act
        var act = () => _serializer.Deserialize<SimpleMessage>(Array.Empty<byte>());

        // Assert - MemoryPack throws on empty bytes
        act.Should().Throw<MemoryPack.MemoryPackSerializationException>();
    }

    [Fact]
    public void Serialize_WithDifferentTypes_ShouldWork()
    {
        // Arrange
        var msg1 = new TypeA { ValueA = "A" };
        var msg2 = new TypeB { ValueB = 123 };

        // Act
        var bytes1 = _serializer.Serialize(msg1, typeof(TypeA));
        var bytes2 = _serializer.Serialize(msg2, typeof(TypeB));

        // Assert
        bytes1.Should().NotBeEmpty();
        bytes2.Should().NotBeEmpty();
        bytes1.Should().NotBeEquivalentTo(bytes2);
    }

    [Fact]
    public void RoundTrip_LargeObject_ShouldPreserveData()
    {
        // Arrange
        var original = new LargeMessage
        {
            Data = Enumerable.Range(0, 1000).Select(i => $"Item-{i}").ToList(),
            Numbers = Enumerable.Range(0, 1000).ToArray()
        };

        // Act
        var bytes = _serializer.Serialize(original, typeof(LargeMessage));
        var result = _serializer.Deserialize<LargeMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Data.Should().HaveCount(1000);
        result.Numbers.Should().HaveCount(1000);
    }

    [Fact]
    public void Serialize_WithNullableProperties_ShouldWork()
    {
        // Arrange
        var obj = new NullableMessage
        {
            RequiredValue = "Required",
            OptionalValue = null,
            OptionalNumber = null
        };

        // Act
        var bytes = _serializer.Serialize(obj, typeof(NullableMessage));
        var result = _serializer.Deserialize<NullableMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.RequiredValue.Should().Be("Required");
        result.OptionalValue.Should().BeNull();
        result.OptionalNumber.Should().BeNull();
    }

    [Fact]
    public void Serialize_WithDateTimeOffset_ShouldPreserve()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var obj = new TimestampMessage { Timestamp = timestamp };

        // Act
        var bytes = _serializer.Serialize(obj, typeof(TimestampMessage));
        var result = _serializer.Deserialize<TimestampMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Timestamp.Should().BeCloseTo(timestamp, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Serialize_WithGuid_ShouldPreserve()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var obj = new GuidMessage { Id = guid };

        // Act
        var bytes = _serializer.Serialize(obj, typeof(GuidMessage));
        var result = _serializer.Deserialize<GuidMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(guid);
    }

    [Fact]
    public void Serialize_WithEnum_ShouldPreserve()
    {
        // Arrange
        var obj = new EnumMessage { Status = MessageStatus.Completed };

        // Act
        var bytes = _serializer.Serialize(obj, typeof(EnumMessage));
        var result = _serializer.Deserialize<EnumMessage>(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(MessageStatus.Completed);
    }

    #region Test Types

    [MemoryPackable]
    private partial class SimpleMessage
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [MemoryPackable]
    private partial class ComplexMessage
    {
        public int Id { get; set; }
        public List<string> Items { get; set; } = new();
        public NestedData? Nested { get; set; }
    }

    [MemoryPackable]
    private partial class NestedData
    {
        public int Value { get; set; }
    }

    [MemoryPackable]
    private partial class TypeA
    {
        public string ValueA { get; set; } = "";
    }

    [MemoryPackable]
    private partial class TypeB
    {
        public int ValueB { get; set; }
    }

    [MemoryPackable]
    private partial class LargeMessage
    {
        public List<string> Data { get; set; } = new();
        public int[] Numbers { get; set; } = Array.Empty<int>();
    }

    [MemoryPackable]
    private partial class NullableMessage
    {
        public string RequiredValue { get; set; } = "";
        public string? OptionalValue { get; set; }
        public int? OptionalNumber { get; set; }
    }

    [MemoryPackable]
    private partial class TimestampMessage
    {
        public DateTimeOffset Timestamp { get; set; }
    }

    [MemoryPackable]
    private partial class GuidMessage
    {
        public Guid Id { get; set; }
    }

    [MemoryPackable]
    private partial class EnumMessage
    {
        public MessageStatus Status { get; set; }
    }

    private enum MessageStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    #endregion
}
