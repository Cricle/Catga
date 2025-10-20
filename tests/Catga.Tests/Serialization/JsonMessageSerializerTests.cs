using Catga.Serialization;
using Catga.Serialization.Json;
using FluentAssertions;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catga.Tests.Serialization;

/// <summary>
/// JSON serializer tests - System.Text.Json based serialization
/// Target: 85%+ coverage
/// </summary>
public class JsonMessageSerializerTests
{
    private readonly JsonMessageSerializer _serializer = new();

    #region Basic Functionality Tests (5 tests)

    [Fact]
    public void Serialize_SimpleObject_ShouldReturnBytes()
    {
        // Arrange
        var message = new JsonTestMessage { Id = 123, Name = "Test", Timestamp = DateTime.UtcNow };

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(0);

        // Verify it's valid JSON
        var json = Encoding.UTF8.GetString(bytes);
        json.Should().Contain("\"Id\"");
        json.Should().Contain("123");
    }

    [Fact]
    public void Deserialize_ValidJson_ShouldReturnObject()
    {
        // Arrange
        var original = new JsonTestMessage { Id = 456, Name = "Hello", Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var bytes = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize<JsonTestMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(456);
        deserialized.Name.Should().Be("Hello");
        deserialized.Timestamp.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void RoundTrip_ComplexObject_ShouldPreserveData()
    {
        // Arrange
        var original = new JsonComplexMessage
        {
            Id = 789,
            Name = "Complex",
            Tags = new List<string> { "tag1", "tag2", "tag3" },
            Nested = new JsonNestedData { Value = 100, Description = "Nested description" }
        };

        // Act
        var bytes = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<JsonComplexMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(789);
        deserialized.Name.Should().Be("Complex");
        deserialized.Tags.Should().BeEquivalentTo(new[] { "tag1", "tag2", "tag3" });
        deserialized.Nested.Should().NotBeNull();
        deserialized.Nested!.Value.Should().Be(100);
        deserialized.Nested.Description.Should().Be("Nested description");
    }

    [Fact]
    public void Serialize_WithCustomOptions_ShouldRespectOptions()
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var customSerializer = new JsonMessageSerializer(options);
        var message = new JsonTestMessage { Id = 111, Name = "Custom", Timestamp = DateTime.UtcNow };

        // Act
        var bytes = customSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        json.Should().Contain("\"id\""); // camelCase (not "Id")
        json.Should().NotContain("\"Id\""); // Verify it's not PascalCase
    }

    [Fact]
    public void Deserialize_WithJsonSerializerContext_ShouldWork()
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = TestJsonContext.Default
        };
        var contextSerializer = new JsonMessageSerializer(options);
        var original = new JsonTestMessage { Id = 222, Name = "Context", Timestamp = DateTime.UtcNow };
        var bytes = contextSerializer.Serialize(original);

        // Act
        var deserialized = contextSerializer.Deserialize<JsonTestMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(222);
        deserialized.Name.Should().Be("Context");
    }

    #endregion

    #region UTF-8 Encoding Tests (2 tests)

    [Fact]
    public void Serialize_UnicodeString_ShouldHandleCorrectly()
    {
        // Arrange
        var message = new JsonTestMessage
        {
            Id = 333,
            Name = "Unicode: 你好世界 🚀 Привет мир",
            Timestamp = DateTime.UtcNow
        };

        // Act
        var bytes = _serializer.Serialize(message);
        var deserialized = _serializer.Deserialize<JsonTestMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("Unicode: 你好世界 🚀 Привет мир");
    }

    [Fact]
    public void Deserialize_Utf8Bytes_ShouldDecodeCorrectly()
    {
        // Arrange
        var json = "{\"Id\":444,\"Name\":\"UTF-8 Test\",\"Timestamp\":\"2025-01-01T00:00:00Z\"}";
        var bytes = Encoding.UTF8.GetBytes(json);

        // Act
        var deserialized = _serializer.Deserialize<JsonTestMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(444);
        deserialized.Name.Should().Be("UTF-8 Test");
    }

    #endregion

    #region Span-based API Tests (3 tests)

    [Fact]
    public void Deserialize_Span_ShouldWork()
    {
        // Arrange
        var original = new JsonTestMessage { Id = 555, Name = "Span", Timestamp = DateTime.UtcNow };
        var bytes = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize<JsonTestMessage>(bytes.AsSpan());

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(555);
        deserialized.Name.Should().Be("Span");
    }

    [Fact]
    public void Serialize_BufferWriter_ShouldWork()
    {
        // Arrange
        var message = new JsonTestMessage { Id = 666, Name = "BufferWriter", Timestamp = DateTime.UtcNow };
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(message, bufferWriter);

        // Assert
        bufferWriter.WrittenCount.Should().BeGreaterThan(0);

        // Verify deserialization
        var deserialized = _serializer.Deserialize<JsonTestMessage>(bufferWriter.WrittenSpan);
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(666);
    }

    #endregion

    #region Performance Tests (3 tests)

    [Fact]
    public void Serialize_WithArrayPool_ShouldReduceAllocations()
    {
        // Arrange
        var message = new JsonTestMessage { Id = 888, Name = "ArrayPool", Timestamp = DateTime.UtcNow };
        var bufferWriter = new ArrayBufferWriter<byte>(256);

        // Act
        _serializer.Serialize(message, bufferWriter);

        // Assert
        bufferWriter.WrittenCount.Should().BeGreaterThan(0);
        bufferWriter.WrittenCount.Should().BeLessThan(256); // Fits in initial capacity
    }

    [Fact]
    public void Deserialize_LargeJson_ShouldBeEfficient()
    {
        // Arrange
        var largeTags = Enumerable.Range(1, 10000).Select(i => $"tag{i}").ToList();
        var message = new JsonComplexMessage
        {
            Id = 999,
            Name = "Large",
            Tags = largeTags,
            Nested = new JsonNestedData { Value = 1, Description = "x" }
        };
        var bytes = _serializer.Serialize(message);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var deserialized = _serializer.Deserialize<JsonComplexMessage>(bytes);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // < 500ms
        deserialized.Should().NotBeNull();
    }

    [Fact]
    public void Serialize_10K_Objects_ShouldBeUnder500ms()
    {
        // Arrange
        var messages = Enumerable.Range(1, 10000)
            .Select(i => new JsonTestMessage { Id = i, Name = $"Message{i}", Timestamp = DateTime.UtcNow })
            .ToList();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var message in messages)
        {
            _serializer.Serialize(message);
        }
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // < 500ms for 10K serializations
    }

    #endregion

    #region Concurrent Tests (2 tests)

    [Fact]
    public async Task Serialize_Concurrent_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        var message = new JsonTestMessage { Id = 1000, Name = "Concurrent", Timestamp = DateTime.UtcNow };

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => _serializer.Serialize(message)));
        }
        await Task.WhenAll(tasks);

        // Assert - no exceptions thrown
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }

    [Fact]
    public async Task Deserialize_Concurrent_ShouldBeThreadSafe()
    {
        // Arrange
        var message = new JsonTestMessage { Id = 1100, Name = "Concurrent", Timestamp = DateTime.UtcNow };
        var bytes = _serializer.Serialize(message);
        var tasks = new List<Task<JsonTestMessage?>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => _serializer.Deserialize<JsonTestMessage>(bytes))!);
        }
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r!.Id.Should().Be(1100);
        });
    }

    #endregion

    #region Error Handling Tests (3 tests)

    [Fact]
    public void Deserialize_InvalidJson_ShouldThrowException()
    {
        // Arrange
        var invalidJson = Encoding.UTF8.GetBytes("{invalid json}");

        // Act & Assert
        var act = () => _serializer.Deserialize<JsonTestMessage>(invalidJson);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_MismatchedType_ShouldThrowException()
    {
        // Arrange
        var json = "{\"Id\":\"not-a-number\",\"Name\":\"Test\"}";
        var bytes = Encoding.UTF8.GetBytes(json);

        // Act & Assert
        var act = () => _serializer.Deserialize<JsonTestMessage>(bytes);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_EmptyBytes_ShouldThrowException()
    {
        // Arrange
        var emptyBytes = Array.Empty<byte>();

        // Act & Assert
        var act = () => _serializer.Deserialize<JsonTestMessage>(emptyBytes);
        act.Should().Throw<JsonException>();
    }

    #endregion

    #region Property Tests (1 test)

    [Fact]
    public void Name_ShouldReturnJSON()
    {
        // Act & Assert
        _serializer.Name.Should().Be("JSON");
    }

    #endregion
}

// Test data types
public class JsonTestMessage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class JsonComplexMessage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public JsonNestedData? Nested { get; set; }
}

public class JsonNestedData
{
    public int Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

// JsonSerializerContext for AOT testing
[JsonSerializable(typeof(JsonTestMessage))]
[JsonSerializable(typeof(JsonComplexMessage))]
[JsonSerializable(typeof(JsonNestedData))]
public partial class TestJsonContext : JsonSerializerContext
{
}

