using Catga;
using Catga.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Serialization;

/// <summary>
/// SerializationHelper测试 - 提升覆盖率从72.9%到90%+
/// 测试Base64编码/解码的不同路径：stackalloc、ArrayPool、fallback
/// </summary>
public class SerializationHelperTests
{
    private readonly IMessageSerializer _mockSerializer;

    public SerializationHelperTests()
    {
        _mockSerializer = Substitute.For<IMessageSerializer>();
    }

    // ==================== Serialize Tests ====================

    [Fact]
    public void Serialize_WithNullSerializer_ShouldThrowArgumentNullException()
    {
        // Arrange
        var obj = new TestMessage { Data = "test" };

        // Act
        var act = () => SerializationHelper.Serialize(obj, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Serialize_WithSmallObject_ShouldReturnBase64String()
    {
        // Arrange - Small data (< 256 bytes when Base64 encoded)
        var obj = new TestMessage { Data = "small" };
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        _mockSerializer.Serialize(obj).Returns(bytes);

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public void Serialize_WithLargeObject_ShouldUseArrayPool()
    {
        // Arrange - Large data (> 256 bytes Base64 = ~192 bytes raw data)
        var obj = new TestMessage { Data = "large" };
        var largeBytes = new byte[200]; // This will create Base64 > 256 bytes
        for (int i = 0; i < largeBytes.Length; i++)
            largeBytes[i] = (byte)(i % 256);
        _mockSerializer.Serialize(obj).Returns(largeBytes);

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(Convert.ToBase64String(largeBytes));
    }

    [Fact]
    public void Serialize_WithEmptyBytes_ShouldReturnEmptyString()
    {
        // Arrange
        var obj = new TestMessage { Data = "empty" };
        _mockSerializer.Serialize(obj).Returns(Array.Empty<byte>());

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Serialize_WithExactlyStackAllocThreshold_ShouldUseStackAlloc()
    {
        // Arrange - Exactly at threshold (256 Base64 bytes = 192 raw bytes)
        var obj = new TestMessage { Data = "threshold" };
        var bytes = new byte[192]; // Base64 will be exactly 256 bytes
        _mockSerializer.Serialize(obj).Returns(bytes);

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var decoded = Convert.FromBase64String(result);
        decoded.Should().HaveCount(192);
    }

    [Fact]
    public void Serialize_WithOnePastThreshold_ShouldUseArrayPool()
    {
        // Arrange - One past threshold (257 Base64 bytes = 193 raw bytes)
        var obj = new TestMessage { Data = "past_threshold" };
        var bytes = new byte[193]; // Base64 will be 260 bytes (> 256)
        _mockSerializer.Serialize(obj).Returns(bytes);

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var decoded = Convert.FromBase64String(result);
        decoded.Should().HaveCount(193);
    }

    // ==================== Deserialize Tests ====================

    [Fact]
    public void Deserialize_WithNullSerializer_ShouldThrowArgumentNullException()
    {
        // Arrange
        var base64 = "dGVzdA==";

        // Act
        var act = () => SerializationHelper.Deserialize<TestMessage>(base64, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Deserialize_WithNullData_ShouldReturnDefault()
    {
        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(null!, _mockSerializer);

        // Assert
        result.Should().BeNull();
        _mockSerializer.DidNotReceive().Deserialize<TestMessage>(Arg.Any<byte[]>());
    }

    [Fact]
    public void Deserialize_WithEmptyData_ShouldReturnDefault()
    {
        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(string.Empty, _mockSerializer);

        // Assert
        result.Should().BeNull();
        _mockSerializer.DidNotReceive().Deserialize<TestMessage>(Arg.Any<byte[]>());
    }

    [Fact]
    public void Deserialize_WithSmallBase64_ShouldUseStackAlloc()
    {
        // Arrange - Small Base64 string (< 256 bytes when decoded)
        var expectedMessage = new TestMessage { Data = "small" };
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var base64 = Convert.ToBase64String(bytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(base64, _mockSerializer);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(expectedMessage);
    }

    [Fact]
    public void Deserialize_WithLargeBase64_ShouldUseFallback()
    {
        // Arrange - Large Base64 string (> 256 bytes when decoded = > 342 Base64 chars)
        var expectedMessage = new TestMessage { Data = "large" };
        var largeBytes = new byte[300]; // Will decode to 300 bytes (> 256)
        var base64 = Convert.ToBase64String(largeBytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(base64, _mockSerializer);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(expectedMessage);
    }

    [Fact]
    public void Deserialize_WithExactlyThreshold_ShouldUseStackAlloc()
    {
        // Arrange - Exactly at threshold (256 bytes = 342 Base64 chars)
        var expectedMessage = new TestMessage { Data = "threshold" };
        var bytes = new byte[256];
        var base64 = Convert.ToBase64String(bytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(base64, _mockSerializer);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_WithOnePastThreshold_ShouldUseFallback()
    {
        // Arrange - One past threshold (257 bytes)
        var expectedMessage = new TestMessage { Data = "past" };
        var bytes = new byte[257];
        var base64 = Convert.ToBase64String(bytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(base64, _mockSerializer);

        // Assert
        result.Should().NotBeNull();
    }

    // ==================== TryDeserialize Tests ====================

    [Fact]
    public void TryDeserialize_WithNullSerializer_ShouldThrowArgumentNullException()
    {
        // Arrange
        var base64 = "dGVzdA==";

        // Act
        var act = () => SerializationHelper.TryDeserialize<TestMessage>(base64, out _, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryDeserialize_WithValidData_ShouldReturnTrueAndResult()
    {
        // Arrange
        var expectedMessage = new TestMessage { Data = "valid" };
        var bytes = new byte[] { 1, 2, 3 };
        var base64 = Convert.ToBase64String(bytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var success = SerializationHelper.TryDeserialize<TestMessage>(base64, out var result, _mockSerializer);

        // Assert
        success.Should().BeTrue();
        result.Should().NotBeNull();
        result.Should().Be(expectedMessage);
    }

    [Fact]
    public void TryDeserialize_WithDeserializerThrowingException_ShouldReturnFalse()
    {
        // Arrange
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>())
            .Returns(_ => throw new InvalidOperationException("Deserialization failed"));

        // Act
        var success = SerializationHelper.TryDeserialize<TestMessage>(base64, out var result, _mockSerializer);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_WithNullResult_ShouldReturnFalse()
    {
        // Arrange
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns((TestMessage?)null);

        // Act
        var success = SerializationHelper.TryDeserialize<TestMessage>(base64, out var result, _mockSerializer);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserialize_WithEmptyData_ShouldReturnFalse()
    {
        // Act
        var success = SerializationHelper.TryDeserialize<TestMessage>(string.Empty, out var result, _mockSerializer);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    // ==================== Round-trip Tests ====================

    [Fact]
    public void SerializeDeserialize_ShouldRoundTrip()
    {
        // Arrange
        var original = new TestMessage { Data = "roundtrip" };
        var bytes = new byte[] { 10, 20, 30 };
        _mockSerializer.Serialize(original).Returns(bytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(original);

        // Act
        var serialized = SerializationHelper.Serialize(original, _mockSerializer);
        var deserialized = SerializationHelper.Deserialize<TestMessage>(serialized, _mockSerializer);

        // Assert
        deserialized.Should().Be(original);
    }

    [Fact]
    public void SerializeDeserialize_WithLargeData_ShouldRoundTrip()
    {
        // Arrange - Large data to trigger ArrayPool path
        var original = new TestMessage { Data = "large_roundtrip" };
        var largeBytes = new byte[300];
        for (int i = 0; i < largeBytes.Length; i++)
            largeBytes[i] = (byte)(i % 256);

        _mockSerializer.Serialize(original).Returns(largeBytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(original);

        // Act
        var serialized = SerializationHelper.Serialize(original, _mockSerializer);
        var deserialized = SerializationHelper.Deserialize<TestMessage>(serialized, _mockSerializer);

        // Assert
        deserialized.Should().Be(original);
        serialized.Length.Should().BeGreaterThan(342); // Base64 of 300 bytes
    }

    // ==================== Edge Cases ====================

    [Fact]
    public void Serialize_WithSingleByte_ShouldWork()
    {
        // Arrange
        var obj = new TestMessage { Data = "single" };
        _mockSerializer.Serialize(obj).Returns(new byte[] { 42 });

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var decoded = Convert.FromBase64String(result);
        decoded.Should().Equal(42);
    }

    [Fact]
    public void Deserialize_WithSingleCharBase64_ShouldWork()
    {
        // Arrange - "AA==" is valid single byte Base64
        var base64 = "AA==";
        var expectedMessage = new TestMessage { Data = "single_char" };
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(base64, _mockSerializer);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void TryDeserialize_WithMultipleExceptionTypes_ShouldReturnFalse()
    {
        // Arrange
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>())
            .Returns(_ => throw new ArgumentException("Invalid argument"));

        // Act
        var success = SerializationHelper.TryDeserialize<TestMessage>(base64, out var result, _mockSerializer);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Serialize_WithVeryLargeObject_ShouldUseFallback()
    {
        // Arrange - Very large data to ensure fallback path
        var obj = new TestMessage { Data = "very_large" };
        var veryLargeBytes = new byte[1000];
        _mockSerializer.Serialize(obj).Returns(veryLargeBytes);

        // Act
        var result = SerializationHelper.Serialize(obj, _mockSerializer);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(1300); // Base64 of 1000 bytes
    }

    [Fact]
    public void Deserialize_WithVeryLargeBase64_ShouldUseFallback()
    {
        // Arrange
        var expectedMessage = new TestMessage { Data = "very_large" };
        var veryLargeBytes = new byte[1000];
        var base64 = Convert.ToBase64String(veryLargeBytes);
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(expectedMessage);

        // Act
        var result = SerializationHelper.Deserialize<TestMessage>(base64, _mockSerializer);

        // Assert
        result.Should().NotBeNull();
    }

    // ==================== Concurrent Access Tests ====================

    [Fact]
    public async Task SerializationHelper_MultipleConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var messages = Enumerable.Range(1, 50)
            .Select(i => new TestMessage { Data = $"message{i}" })
            .ToArray();

        _mockSerializer.Serialize(Arg.Any<TestMessage>())
            .Returns(callInfo => System.Text.Encoding.UTF8.GetBytes(callInfo.Arg<TestMessage>().Data));
        _mockSerializer.Deserialize<TestMessage>(Arg.Any<byte[]>())
            .Returns(callInfo => new TestMessage { Data = System.Text.Encoding.UTF8.GetString(callInfo.Arg<byte[]>()) });

        // Act - Call serialize/deserialize concurrently
        var tasks = messages.Select(msg => Task.Run(() =>
        {
            var serialized = SerializationHelper.Serialize(msg, _mockSerializer);
            var deserialized = SerializationHelper.Deserialize<TestMessage>(serialized, _mockSerializer);
            return deserialized!.Data;
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(50);
        results.Should().Contain("message1");
        results.Should().Contain("message50");
    }

    // ==================== Test Helpers ====================

    public record TestMessage
    {
        public string Data { get; init; } = string.Empty;
    }
}

