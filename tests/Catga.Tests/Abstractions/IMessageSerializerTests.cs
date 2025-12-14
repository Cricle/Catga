using Catga.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Abstractions;

public class IMessageSerializerTests
{
    [Fact]
    public void IMessageSerializer_CanBeMocked()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Should().NotBeNull();
    }

    [Fact]
    public void Serialize_ReturnsBytes()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
        serializer.Serialize(Arg.Any<object>()).Returns(expectedBytes);

        var result = serializer.Serialize(new { Data = "test" });

        result.Should().BeEquivalentTo(expectedBytes);
    }

    [Fact]
    public void Deserialize_ReturnsObject()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var input = new byte[] { 1, 2, 3 };
        var expected = new TestMessage { Value = "test" };
        serializer.Deserialize<TestMessage>(input).Returns(expected);

        var result = serializer.Deserialize<TestMessage>(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void Serialize_CalledWithCorrectParameter()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var message = new TestMessage { Value = "hello" };

        serializer.Serialize(message);

        serializer.Received(1).Serialize(message);
    }

    [Fact]
    public void Deserialize_CalledWithCorrectParameter()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var bytes = new byte[] { 1, 2, 3 };

        serializer.Deserialize<TestMessage>(bytes);

        serializer.Received(1).Deserialize<TestMessage>(bytes);
    }

    [Fact]
    public void Serialize_EmptyObject_ReturnsBytes()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<object>()).Returns(Array.Empty<byte>());

        var result = serializer.Serialize(new { });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_EmptyBytes_ReturnsNull()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize<TestMessage>(Array.Empty<byte>()).Returns((TestMessage?)null);

        var result = serializer.Deserialize<TestMessage>(Array.Empty<byte>());

        result.Should().BeNull();
    }

    private class TestMessage
    {
        public string Value { get; set; } = "";
    }
}
