using Catga.Core;
using Catga.Messages;
using Catga.Serialization;
using Catga.Transport;
using Catga.Transport.Nats;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryPack;
using NATS.Client.Core;
using NSubstitute;

namespace Catga.Tests.Transport;

/// <summary>
/// NATS message transport tests - QoS delegation to NATS, serialization integration
/// Target: 80%+ coverage (mocked NATS connection)
/// </summary>
public class NatsMessageTransportTests
{
    private readonly INatsConnection _mockConnection;
    private readonly IMessageSerializer _mockSerializer;
    private readonly ILogger<NatsMessageTransport> _mockLogger;
    private readonly NatsMessageTransport _transport;

    public NatsMessageTransportTests()
    {
        _mockConnection = Substitute.For<INatsConnection>();
        _mockSerializer = Substitute.For<IMessageSerializer>();
        _mockLogger = Substitute.For<ILogger<NatsMessageTransport>>();
        _transport = new NatsMessageTransport(_mockConnection, _mockSerializer, _mockLogger);
    }

    #region Basic Publish Tests (3 tests)

    [Fact]
    public async Task PublishAsync_QoS0_ShouldUseNatsCorePubSub()
    {
        // Arrange
        var message = new NatsQoS0Message(123, "QoS0");
        var serializedData = new byte[] { 1, 2, 3 };
        _mockSerializer.Serialize(message).Returns(serializedData);

        // Act
        await _transport.PublishAsync(message);

        // Assert
        await _mockConnection.Received(1).PublishAsync(
            Arg.Is<string>(s => s.Contains("NatsQoS0Message")),
            Arg.Is<byte[]>(b => b.SequenceEqual(serializedData)),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task PublishAsync_WithCustomContext_ShouldIncludeContextInHeaders()
    {
        // Arrange
        var message = new NatsQoS0Message(456, "Context");
        var context = new TransportContext
        {
            MessageId = "custom-id-123",
            CorrelationId = "correlation-456",
            MessageType = "CustomType"
        };
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        NatsHeaders? capturedHeaders = null;
        await _mockConnection.PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Do<NatsHeaders>(h => capturedHeaders = h),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await _transport.PublishAsync(message, context);

        // Assert
        capturedHeaders.Should().NotBeNull();
        capturedHeaders!["MessageId"].ToString().Should().Be("custom-id-123");
        capturedHeaders["CorrelationId"].ToString().Should().Be("correlation-456");
    }

    [Fact]
    public async Task SendAsync_ShouldDelegateToPublishAsync()
    {
        // Arrange
        var message = new NatsQoS0Message(789, "Send");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        // Act
        await _transport.SendAsync(message, "destination");

        // Assert
        await _mockConnection.Received(1).PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    #endregion

    #region Serialization Tests (3 tests)

    [Fact]
    public async Task PublishAsync_ShouldSerializeMessage()
    {
        // Arrange
        var message = new NatsQoS0Message(111, "Serialize");
        var expectedData = new byte[] { 10, 20, 30 };
        _mockSerializer.Serialize(message).Returns(expectedData);

        // Act
        await _transport.PublishAsync(message);

        // Assert
        _mockSerializer.Received(1).Serialize(message);
        await _mockConnection.Received(1).PublishAsync(
            Arg.Any<string>(),
            Arg.Is<byte[]>(b => b.SequenceEqual(expectedData)),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task PublishAsync_SerializerReturnsNull_ShouldHandleGracefully()
    {
        // Arrange
        var message = new NatsQoS0Message(222, "NullData");
        _mockSerializer.Serialize(message).Returns((byte[]?)null!);

        // Act & Assert - should not throw
        var act = async () => await _transport.PublishAsync(message);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_SerializerThrows_ShouldPropagateException()
    {
        // Arrange
        var message = new NatsQoS0Message(333, "SerializerError");
        _mockSerializer.When(x => x.Serialize(message)).Do(_ => throw new InvalidOperationException("Serialization failed"));

        // Act & Assert
        var act = async () => await _transport.PublishAsync(message);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Serialization failed");
    }

    #endregion

    #region Batch Operations Tests (2 tests)

    [Fact]
    public async Task PublishBatchAsync_MultipleMessages_ShouldPublishAll()
    {
        // Arrange
        var messages = new[]
        {
            new NatsQoS0Message(1, "Batch1"),
            new NatsQoS0Message(2, "Batch2"),
            new NatsQoS0Message(3, "Batch3")
        };
        _mockSerializer.Serialize(Arg.Any<NatsQoS0Message>()).Returns(new byte[] { 1, 2, 3 });

        // Act
        await _transport.PublishBatchAsync(messages);

        // Assert
        await _mockConnection.Received(3).PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task SendBatchAsync_ShouldDelegateToPublishBatchAsync()
    {
        // Arrange
        var messages = new[]
        {
            new NatsQoS0Message(10, "SendBatch1"),
            new NatsQoS0Message(20, "SendBatch2")
        };
        _mockSerializer.Serialize(Arg.Any<NatsQoS0Message>()).Returns(new byte[] { 1, 2, 3 });

        // Act
        await _transport.SendBatchAsync(messages, "destination");

        // Assert
        await _mockConnection.Received(2).PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    #endregion

    #region Subject Generation Tests (3 tests)

    [Fact]
    public async Task PublishAsync_ShouldGenerateCorrectSubject()
    {
        // Arrange
        var message = new NatsQoS0Message(444, "Subject");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        string? capturedSubject = null;
        await _mockConnection.PublishAsync(
            Arg.Do<string>(s => capturedSubject = s),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await _transport.PublishAsync(message);

        // Assert
        capturedSubject.Should().NotBeNull();
        capturedSubject.Should().Contain("catga"); // Default prefix
        capturedSubject.Should().Contain("NatsQoS0Message"); // Message type
    }

    [Fact]
    public async Task PublishAsync_WithCustomPrefix_ShouldUseCustomPrefix()
    {
        // Arrange
        var customTransport = new NatsMessageTransport(
            _mockConnection,
            _mockSerializer,
            _mockLogger,
            new NatsTransportOptions { SubjectPrefix = "custom-prefix" }
        );
        var message = new NatsQoS0Message(555, "CustomPrefix");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        string? capturedSubject = null;
        await _mockConnection.PublishAsync(
            Arg.Do<string>(s => capturedSubject = s),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await customTransport.PublishAsync(message);

        // Assert
        capturedSubject.Should().NotBeNull();
        capturedSubject.Should().Contain("custom-prefix");
    }

    [Fact]
    public async Task PublishAsync_SameMessageType_ShouldCacheSubject()
    {
        // Arrange
        var message1 = new NatsQoS0Message(666, "Cache1");
        var message2 = new NatsQoS0Message(777, "Cache2");
        _mockSerializer.Serialize(Arg.Any<NatsQoS0Message>()).Returns(new byte[] { 1, 2, 3 });

        var capturedSubjects = new List<string>();
        await _mockConnection.PublishAsync(
            Arg.Do<string>(s => capturedSubjects.Add(s)),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await _transport.PublishAsync(message1);
        await _transport.PublishAsync(message2);

        // Assert - both should use the same cached subject
        capturedSubjects.Should().HaveCount(2);
        capturedSubjects[0].Should().Be(capturedSubjects[1]);
    }

    #endregion

    #region Header Tests (3 tests)

    [Fact]
    public async Task PublishAsync_ShouldIncludeMessageIdInHeaders()
    {
        // Arrange
        var message = new NatsQoS0Message(888, "MessageId");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        NatsHeaders? capturedHeaders = null;
        await _mockConnection.PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Do<NatsHeaders>(h => capturedHeaders = h),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await _transport.PublishAsync(message);

        // Assert
        capturedHeaders.Should().NotBeNull();
        capturedHeaders!["MessageId"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PublishAsync_ShouldIncludeMessageTypeInHeaders()
    {
        // Arrange
        var message = new NatsQoS0Message(999, "MessageType");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        NatsHeaders? capturedHeaders = null;
        await _mockConnection.PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Do<NatsHeaders>(h => capturedHeaders = h),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await _transport.PublishAsync(message);

        // Assert
        capturedHeaders.Should().NotBeNull();
        capturedHeaders!["MessageType"].Should().Contain("NatsQoS0Message");
    }

    [Fact]
    public async Task PublishAsync_ShouldIncludeQoSInHeaders()
    {
        // Arrange
        var message = new NatsQoS0Message(1000, "QoS");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });

        NatsHeaders? capturedHeaders = null;
        await _mockConnection.PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Do<NatsHeaders>(h => capturedHeaders = h),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );

        // Act
        await _transport.PublishAsync(message);

        // Assert
        capturedHeaders.Should().NotBeNull();
        capturedHeaders!["QoS"].Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Property Tests (3 tests)

    [Fact]
    public void Name_ShouldReturnNATS()
    {
        // Act & Assert
        _transport.Name.Should().Be("NATS");
    }

    [Fact]
    public void BatchOptions_ShouldBeNull()
    {
        // Act & Assert
        _transport.BatchOptions.Should().BeNull();
    }

    [Fact]
    public void CompressionOptions_ShouldBeNull()
    {
        // Act & Assert
        _transport.CompressionOptions.Should().BeNull();
    }

    #endregion

    #region Error Handling Tests (2 tests)

    [Fact]
    public async Task PublishAsync_ConnectionThrows_ShouldPropagateException()
    {
        // Arrange
        var message = new NatsQoS0Message(1111, "ConnectionError");
        _mockSerializer.Serialize(message).Returns(new byte[] { 1, 2, 3 });
        _mockConnection.When(x => x.PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        )).Do(_ => throw new InvalidOperationException("Connection failed"));

        // Act & Assert
        var act = async () => await _transport.PublishAsync(message);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Connection failed");
    }

    [Fact]
    public async Task PublishBatchAsync_PartialFailure_ShouldStopAtFirstError()
    {
        // Arrange
        var messages = new[]
        {
            new NatsQoS0Message(1, "Batch1"),
            new NatsQoS0Message(2, "Batch2"),
            new NatsQoS0Message(3, "Batch3")
        };
        _mockSerializer.Serialize(Arg.Any<NatsQoS0Message>()).Returns(new byte[] { 1, 2, 3 });

        var callCount = 0;
        _mockConnection.When(x => x.PublishAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            headers: Arg.Any<NatsHeaders>(),
            replyTo: Arg.Any<string>(),
            opts: Arg.Any<NatsPubOpts>(),
            cancellationToken: Arg.Any<CancellationToken>()
        )).Do(_ =>
        {
            callCount++;
            if (callCount == 2)
                throw new InvalidOperationException("Second message failed");
        });

        // Act & Assert
        var act = async () => await _transport.PublishBatchAsync(messages);
        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(2); // Should stop after second message
    }

    #endregion
}

// Test message types for NATS
[MemoryPackable]
public partial record NatsQoS0Message(int Id, string Name) : IMessage
{
    public QualityOfService QoS => QualityOfService.AtMostOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

