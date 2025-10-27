using Catga.Abstractions;
using Catga.Core;
using Catga.DistributedId;
using Catga.Outbox;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// OutboxBehavior单元测试
/// 目标覆盖率: 从 0% → 90%+
/// </summary>
public class OutboxBehaviorTests
{
    private readonly ILogger<OutboxBehavior<TestEvent, EmptyResponse>> _mockLogger;
    private readonly IDistributedIdGenerator _mockIdGenerator;
    private readonly IOutboxStore _mockStore;
    private readonly IMessageTransport _mockTransport;
    private readonly IMessageSerializer _mockSerializer;
    private readonly OutboxBehavior<TestEvent, EmptyResponse> _behavior;

    public OutboxBehaviorTests()
    {
        _mockLogger = Substitute.For<ILogger<OutboxBehavior<TestEvent, EmptyResponse>>>();
        _mockIdGenerator = Substitute.For<IDistributedIdGenerator>();
        _mockStore = Substitute.For<IOutboxStore>();
        _mockTransport = Substitute.For<IMessageTransport>();
        _mockSerializer = Substitute.For<IMessageSerializer>();

        _mockTransport.Name.Returns("MockTransport");

        _behavior = new OutboxBehavior<TestEvent, EmptyResponse>(
            _mockLogger,
            _mockIdGenerator,
            _mockStore,
            _mockTransport,
            _mockSerializer);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new OutboxBehavior<TestEvent, EmptyResponse>(
            null!,
            _mockIdGenerator,
            _mockStore,
            _mockTransport,
            _mockSerializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullIdGenerator_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new OutboxBehavior<TestEvent, EmptyResponse>(
            _mockLogger,
            null!,
            _mockStore,
            _mockTransport,
            _mockSerializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("idGenerator");
    }

    [Fact]
    public void Constructor_WithNullPersistence_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new OutboxBehavior<TestEvent, EmptyResponse>(
            _mockLogger,
            _mockIdGenerator,
            null!,
            _mockTransport,
            _mockSerializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("persistence");
    }

    [Fact]
    public void Constructor_WithNullTransport_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new OutboxBehavior<TestEvent, EmptyResponse>(
            _mockLogger,
            _mockIdGenerator,
            _mockStore,
            null!,
            _mockSerializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("transport");
    }

    [Fact]
    public void Constructor_WithNullSerializer_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new OutboxBehavior<TestEvent, EmptyResponse>(
            _mockLogger,
            _mockIdGenerator,
            _mockStore,
            _mockTransport,
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    #endregion

    #region Non-Event Request Tests

    [Fact]
    public async Task HandleAsync_WithNonEventRequest_ShouldSkipOutbox()
    {
        // Arrange
        var behavior = new OutboxBehavior<NonEventRequest, TestResponse>(
            Substitute.For<ILogger<OutboxBehavior<NonEventRequest, TestResponse>>>(),
            _mockIdGenerator,
            _mockStore,
            _mockTransport,
            _mockSerializer);

        var request = new NonEventRequest { Data = "test" };
        var expectedResponse = new TestResponse { Result = "OK" };

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        await _mockStore.DidNotReceive().AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Successful Flow Tests

    [Fact]
    public async Task HandleAsync_WithEvent_ShouldSaveToOutbox()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 123, Data = "test" };
        _mockIdGenerator.Generate().Returns(999L);
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        await _behavior.HandleAsync(@event, next);

        // Assert
        await _mockStore.Received(1).AddAsync(
            Arg.Is<OutboxMessage>(m =>
                m.MessageId == 123 &&
                m.Status == OutboxStatus.Pending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuccessfulProcessing_ShouldPublishAndMarkAsPublished()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 456, Data = "test" };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        var result = await _behavior.HandleAsync(@event, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mockTransport.Received(1).PublishAsync<TestEvent>(
            @event,
            Arg.Any<TransportContext>(),
            Arg.Any<CancellationToken>());
        await _mockStore.Received(1).MarkAsPublishedAsync(456, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldGenerateMessageIdWhenZero()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 0, Data = "test" };
        _mockIdGenerator.Generate().Returns(999L);
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        await _behavior.HandleAsync(@event, next);

        // Assert
        _mockIdGenerator.Received(1).Generate();
        await _mockStore.Received(1).AddAsync(
            Arg.Is<OutboxMessage>(m => m.MessageId == 999L),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldSetCorrectOutboxMessageFields()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 789, CorrelationId = 111, Data = "test" };
        var serializedData = new byte[] { 1, 2, 3, 4, 5 };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(serializedData);

        OutboxMessage? capturedMessage = null;
        _mockStore.AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(x => capturedMessage = x.Arg<OutboxMessage>());

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        await _behavior.HandleAsync(@event, next);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.MessageId.Should().Be(789);
        capturedMessage.CorrelationId.Should().Be(111);
        capturedMessage.MessageType.Should().Contain("TestEvent");
        capturedMessage.Payload.Should().BeEquivalentTo(serializedData);
        capturedMessage.Status.Should().Be(OutboxStatus.Pending);
        capturedMessage.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Handler Failure Tests

    [Fact]
    public async Task HandleAsync_HandlerFails_ShouldNotPublish()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 555, Data = "test" };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(
            CatgaResult<EmptyResponse>.Failure("Handler failed"));

        // Act
        var result = await _behavior.HandleAsync(@event, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        await _mockStore.Received(1).AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await _mockTransport.DidNotReceive().PublishAsync<TestEvent>(
            Arg.Any<TestEvent>(),
            Arg.Any<TransportContext>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Transport Failure Tests

    [Fact]
    public async Task HandleAsync_TransportFails_ShouldMarkAsFailed()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 666, Data = "test" };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        _mockTransport.PublishAsync<TestEvent>(
            Arg.Any<TestEvent>(),
            Arg.Any<TransportContext>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("Transport error")));

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        var result = await _behavior.HandleAsync(@event, next);

        // Assert
        result.IsSuccess.Should().BeTrue(); // Handler succeeded
        await _mockStore.Received(1).MarkAsFailedAsync(
            666,
            Arg.Is<string>(s => s.Contains("Transport error")),
            Arg.Any<CancellationToken>());
        await _mockStore.DidNotReceive().MarkAsPublishedAsync(
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Persistence Exception Tests

    [Fact]
    public async Task HandleAsync_PersistenceAddFails_ShouldReturnFailure()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 777, Data = "test" };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        _mockStore.AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("Database error")));

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        var result = await _behavior.HandleAsync(@event, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Database error");
        result.ErrorCode.Should().Be(ErrorCodes.PersistenceFailed);
    }

    [Fact]
    public async Task HandleAsync_PersistenceMarkAsPublishedFails_ShouldMarkAsFailed()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 888, Data = "test" };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        _mockStore.MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("Mark published error")));

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        var result = await _behavior.HandleAsync(@event, next);

        // Assert
        result.IsSuccess.Should().BeTrue(); // Handler succeeded
        await _mockStore.Received(1).MarkAsFailedAsync(
            888,
            Arg.Is<string>(s => s.Contains("Mark published error")),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task HandleAsync_WithCancellationToken_ShouldPassToServices()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 999, Data = "test" };
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        await _behavior.HandleAsync(@event, next, cancellationToken);

        // Assert
        await _mockStore.Received(1).AddAsync(Arg.Any<OutboxMessage>(), cancellationToken);
        await _mockTransport.Received(1).PublishAsync<TestEvent>(
            Arg.Any<TestEvent>(),
            Arg.Any<TransportContext>(),
            cancellationToken);
        await _mockStore.Received(1).MarkAsPublishedAsync(Arg.Any<long>(), cancellationToken);
    }

    #endregion

    #region TransportContext Tests

    [Fact]
    public async Task HandleAsync_ShouldSetCorrectTransportContext()
    {
        // Arrange
        var @event = new TestEvent { MessageId = 1111, CorrelationId = 2222, Data = "test" };
        _mockSerializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });

        TransportContext? capturedContext = null;
        _mockTransport.PublishAsync<TestEvent>(
            Arg.Any<TestEvent>(),
            Arg.Any<TransportContext>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<TransportContext>();
                return ValueTask.CompletedTask.AsTask();
            });

        PipelineDelegate<EmptyResponse> next = () => ValueTask.FromResult(CatgaResult<EmptyResponse>.Success(new EmptyResponse()));

        // Act
        await _behavior.HandleAsync(@event, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Value.MessageId.Should().Be(1111);
        capturedContext.Value.CorrelationId.Should().Be(2222);
        capturedContext.Value.MessageType.Should().Contain("TestEvent");
        capturedContext.Value.SentAt.Should().NotBeNull();
        capturedContext.Value.SentAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Test Helper Classes

    public class TestEvent : IEvent, IRequest<EmptyResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public class NonEventRequest : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public class TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    public class EmptyResponse
    {
    }

    #endregion
}

