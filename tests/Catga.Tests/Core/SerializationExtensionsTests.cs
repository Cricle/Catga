using System.Text;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Moq;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for SerializationExtensions - DRY helper methods
/// </summary>
public class SerializationExtensionsTests
{
    [Fact]
    public void SerializeToJson_ValidObject_ShouldReturnJsonString()
    {
        // Arrange
        var testObj = new TestData { Id = 123, Name = "Test" };
        var expectedBytes = Encoding.UTF8.GetBytes("{\"Id\":123,\"Name\":\"Test\"}");

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Serialize(testObj))
            .Returns(expectedBytes);

        // Act
        var json = mockSerializer.Object.SerializeToJson(testObj);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("123");
        json.Should().Contain("Test");
    }

    [Fact]
    public void SerializeToJson_NullObject_ShouldReturnEmptyString()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();
        TestData? nullData = null;

        // Act
        var json = mockSerializer.SerializeToJson(nullData);

        // Assert
        json.Should().BeEmpty();
    }

    [Fact]
    public void DeserializeFromJson_ValidJson_ShouldReturnObject()
    {
        // Arrange
        var json = "{\"Id\":456,\"Name\":\"Deserialized\"}";
        var expectedObj = new TestData { Id = 456, Name = "Deserialized" };

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(It.IsAny<byte[]>()))
            .Returns(expectedObj);

        // Act
        var result = mockSerializer.Object.DeserializeFromJson<TestData>(json);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(456);
        result.Name.Should().Be("Deserialized");
    }

    [Fact]
    public void DeserializeFromJson_NullJson_ShouldReturnDefault()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();

        // Act
        var result = mockSerializer.DeserializeFromJson<TestData>(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DeserializeFromJson_EmptyJson_ShouldReturnDefault()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();

        // Act
        var result = mockSerializer.DeserializeFromJson<TestData>("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_ValidBytes_ShouldReturnTrueAndResult()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4 };
        var expectedObj = new TestData { Id = 789, Name = "TryDeserialize" };

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(data))
            .Returns(expectedObj);

        // Act
        var success = mockSerializer.Object.TryDeserialize<TestData>(data, out var result);

        // Assert
        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Id.Should().Be(789);
        result.Name.Should().Be("TryDeserialize");
    }

    [Fact]
    public void TryDeserialize_NullBytes_ShouldReturnFalse()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();

        // Act
        var success = mockSerializer.TryDeserialize<TestData>(null!, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_EmptyBytes_ShouldReturnFalse()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();

        // Act
        var success = mockSerializer.TryDeserialize<TestData>(Array.Empty<byte>(), out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_DeserializationThrows_ShouldReturnFalse()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(data))
            .Throws<InvalidOperationException>();

        // Act
        var success = mockSerializer.Object.TryDeserialize<TestData>(data, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_ReturnsNull_ShouldReturnFalse()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var mockSerializer = new Mock<IMessageSerializer>();
        TestData? nullResult = null;
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(data))
            .Returns(nullResult);

        // Act
        var success = mockSerializer.Object.TryDeserialize<TestData>(data, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserializeFromJson_ValidJson_ShouldReturnTrueAndResult()
    {
        // Arrange
        var json = "{\"Id\":999,\"Name\":\"TryJson\"}";
        var expectedObj = new TestData { Id = 999, Name = "TryJson" };

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(It.IsAny<byte[]>()))
            .Returns(expectedObj);

        // Act
        var success = mockSerializer.Object.TryDeserializeFromJson<TestData>(json, out var result);

        // Assert
        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Id.Should().Be(999);
        result.Name.Should().Be("TryJson");
    }

    [Fact]
    public void TryDeserializeFromJson_NullJson_ShouldReturnFalse()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();

        // Act
        var success = mockSerializer.TryDeserializeFromJson<TestData>(null!, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserializeFromJson_EmptyJson_ShouldReturnFalse()
    {
        // Arrange
        var mockSerializer = Mock.Of<IMessageSerializer>();

        // Act
        var success = mockSerializer.TryDeserializeFromJson<TestData>("", out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserializeFromJson_DeserializationThrows_ShouldReturnFalse()
    {
        // Arrange
        var json = "invalid json";
        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(It.IsAny<byte[]>()))
            .Throws<InvalidOperationException>();

        // Act
        var success = mockSerializer.Object.TryDeserializeFromJson<TestData>(json, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void SerializeToJson_ThenDeserializeFromJson_ShouldRoundTrip()
    {
        // Arrange
        var original = new TestData { Id = 12345, Name = "RoundTrip" };
        var jsonBytes = Encoding.UTF8.GetBytes("{\"Id\":12345,\"Name\":\"RoundTrip\"}");

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Serialize(original))
            .Returns(jsonBytes);
        mockSerializer
            .Setup(s => s.Deserialize<TestData>(It.IsAny<byte[]>()))
            .Returns(original);

        // Act
        var json = mockSerializer.Object.SerializeToJson(original);
        var deserialized = mockSerializer.Object.DeserializeFromJson<TestData>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.Name.Should().Be(original.Name);
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}

