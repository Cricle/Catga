using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using System.Buffers;

namespace Catga.Tests.Serialization;

/// <summary>
/// MemoryPack serializer tests - high-performance binary serialization
/// Target: 90%+ coverage
/// </summary>
public class MemoryPackMessageSerializerTests
{
    private readonly MemoryPackMessageSerializer _serializer = new();

    #region Basic Functionality Tests (5 tests)

    [Fact]
    public void Serialize_SimpleObject_ShouldReturnBytes()
    {
        // Arrange
        var message = new TestMessage(123, "Test", DateTime.UtcNow);

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Deserialize_ValidBytes_ShouldReturnObject()
    {
        // Arrange
        var original = new TestMessage(456, "Hello", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var bytes = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize<TestMessage>(bytes);

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
        var original = new ComplexMessage(
            789,
            "Complex",
            new List<string> { "tag1", "tag2", "tag3" },
            new NestedData(100, "Nested description")
        );

        // Act
        var bytes = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<ComplexMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(789);
        deserialized.Name.Should().Be("Complex");
        deserialized.Tags.Should().BeEquivalentTo(new[] { "tag1", "tag2", "tag3" });
        deserialized.Nested.Should().NotBeNull();
        deserialized.Nested.Value.Should().Be(100);
        deserialized.Nested.Description.Should().Be("Nested description");
    }

    [Fact]
    public void Serialize_NullValue_ShouldHandleGracefully()
    {
        // Arrange
        TestMessage? message = null;

        // Act
        var bytes = _serializer.Serialize(message);

        // Assert
        bytes.Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_EmptyBytes_ShouldThrowException()
    {
        // Arrange
        var emptyBytes = Array.Empty<byte>();

        // Act & Assert
        var act = () => _serializer.Deserialize<TestMessage>(emptyBytes);
        act.Should().Throw<Exception>();
    }

    #endregion

    #region Span-based API Tests (3 tests)

    [Fact]
    public void Deserialize_Span_ShouldWork()
    {
        // Arrange
        var original = new TestMessage(111, "Span", DateTime.UtcNow);
        var bytes = _serializer.Serialize(original);

        // Act
        var deserialized = _serializer.Deserialize<TestMessage>(bytes.AsSpan());

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(111);
        deserialized.Name.Should().Be("Span");
    }

    [Fact]
    public void Serialize_BufferWriter_ShouldWork()
    {
        // Arrange
        var message = new TestMessage(222, "BufferWriter", DateTime.UtcNow);
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(message, bufferWriter);

        // Assert
        bufferWriter.WrittenCount.Should().BeGreaterThan(0);

        // Verify deserialization
        var deserialized = _serializer.Deserialize<TestMessage>(bufferWriter.WrittenSpan);
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(222);
    }

    [Fact]
    public void GetSizeEstimate_ShouldReturnReasonableValue()
    {
        // Arrange
        var message = new TestMessage(333, "Size", DateTime.UtcNow);

        // Act
        var estimate = _serializer.GetSizeEstimate(message);

        // Assert
        estimate.Should().BeGreaterThan(0);
        estimate.Should().Be(128); // Default estimate
    }

    #endregion

    #region Complex Object Tests (3 tests)

    [Fact]
    public void Serialize_NestedObject_ShouldWork()
    {
        // Arrange
        var nested = new NestedData(999, "Deep nesting");
        var message = new ComplexMessage(444, "Nested", new List<string> { "a" }, nested);

        // Act
        var bytes = _serializer.Serialize(message);
        var deserialized = _serializer.Deserialize<ComplexMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Nested.Value.Should().Be(999);
        deserialized.Nested.Description.Should().Be("Deep nesting");
    }

    [Fact]
    public void Serialize_CollectionObject_ShouldWork()
    {
        // Arrange
        var tags = Enumerable.Range(1, 100).Select(i => $"tag{i}").ToList();
        var message = new ComplexMessage(555, "Collection", tags, new NestedData(1, "x"));

        // Act
        var bytes = _serializer.Serialize(message);
        var deserialized = _serializer.Deserialize<ComplexMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Tags.Should().HaveCount(100);
        deserialized.Tags.First().Should().Be("tag1");
        deserialized.Tags.Last().Should().Be("tag100");
    }

    [Fact]
    public void Serialize_EmptyCollection_ShouldWork()
    {
        // Arrange
        var message = new ComplexMessage(666, "Empty", new List<string>(), new NestedData(0, ""));

        // Act
        var bytes = _serializer.Serialize(message);
        var deserialized = _serializer.Deserialize<ComplexMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Tags.Should().BeEmpty();
    }

    #endregion

    #region Performance Tests (3 tests)

    [Fact]
    public void Serialize_LargeObject_ShouldBeEfficient()
    {
        // Arrange
        var largeTags = Enumerable.Range(1, 10000).Select(i => $"tag{i}").ToList();
        var message = new ComplexMessage(777, "Large", largeTags, new NestedData(1, "x"));

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var bytes = _serializer.Serialize(message);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100); // < 100ms
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Deserialize_LargeObject_ShouldBeEfficient()
    {
        // Arrange
        var largeTags = Enumerable.Range(1, 10000).Select(i => $"tag{i}").ToList();
        var message = new ComplexMessage(888, "Large", largeTags, new NestedData(1, "x"));
        var bytes = _serializer.Serialize(message);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var deserialized = _serializer.Deserialize<ComplexMessage>(bytes);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100); // < 100ms
        deserialized.Should().NotBeNull();
    }

    [Fact]
    public void Serialize_10K_Objects_ShouldBeUnder100ms()
    {
        // Arrange
        var messages = Enumerable.Range(1, 10000)
            .Select(i => new TestMessage(i, $"Message{i}", DateTime.UtcNow))
            .ToList();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var message in messages)
        {
            _serializer.Serialize(message);
        }
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100); // < 100ms for 10K serializations
    }

    #endregion

    #region Concurrent Tests (2 tests)

    [Fact]
    public async Task Serialize_Concurrent_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        var message = new TestMessage(999, "Concurrent", DateTime.UtcNow);

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
        var message = new TestMessage(1000, "Concurrent", DateTime.UtcNow);
        var bytes = _serializer.Serialize(message);
        var tasks = new List<Task<TestMessage?>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => _serializer.Deserialize<TestMessage>(bytes))!);
        }
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r!.Id.Should().Be(1000);
        });
    }

    #endregion

    #region Property Tests (1 test)

    [Fact]
    public void Name_ShouldReturnMemoryPack()
    {
        // Act & Assert
        _serializer.Name.Should().Be("MemoryPack");
    }

    #endregion
}

// Test data types
[MemoryPackable]
public partial record TestMessage(int Id, string Name, DateTime Timestamp);

[MemoryPackable]
public partial record ComplexMessage(
    int Id,
    string Name,
    List<string> Tags,
    NestedData Nested
);

[MemoryPackable]
public partial record NestedData(int Value, string Description);

