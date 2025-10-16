using Catga.Core;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using FluentAssertions;
using MemoryPack;

namespace Catga.Tests.Integration;

/// <summary>
/// Basic integration tests for core CQRS functionality
/// </summary>
[Trait("Category", "Integration")]
public class BasicIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ICatgaMediator _mediator;

    public BasicIntegrationTests()
    {
        _fixture = new IntegrationTestFixture();
        _mediator = _fixture.Mediator;
    }

    [Fact]
    public async Task Should_Send_Command_And_Receive_Response()
    {
        // Arrange
        var command = new SimpleCommand("test-data");

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.ProcessedData.Should().Be("PROCESSED:test-data");
    }

    [Fact]
    public async Task Should_Publish_Event_To_Multiple_Handlers()
    {
        // Arrange
        var @event = new SimpleEvent("event-data");
        SimpleEventHandler1.ReceivedCount = 0;
        SimpleEventHandler2.ReceivedCount = 0;

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(100); // Wait for async event processing

        // Assert
        SimpleEventHandler1.ReceivedCount.Should().Be(1);
        SimpleEventHandler2.ReceivedCount.Should().Be(1);
    }

    [Fact]
    public async Task Should_Handle_100_Concurrent_Requests()
    {
        // Arrange
        var commands = Enumerable.Range(0, 100)
            .Select(i => new SimpleCommand($"data-{i}"))
            .ToArray();

        // Act
        var tasks = commands.Select(cmd => _mediator.SendAsync(cmd)).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(100);
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Data.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task Should_Handle_SafeRequestHandler_Success()
    {
        // Arrange
        var command = new SafeCommand("valid-data");

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task Should_Handle_SafeRequestHandler_Error()
    {
        // Arrange
        var command = new SafeCommand("error");

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("error");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}

#region Test Messages

[MemoryPackable]
public partial record SimpleCommand(string Data) : IRequest<SimpleResponse>;

[MemoryPackable]
public partial record SimpleResponse(string ProcessedData);

[MemoryPackable]
public partial record SimpleEvent(string Data) : IEvent;

[MemoryPackable]
public partial record SafeCommand(string Data) : IRequest<SafeResponse>;

[MemoryPackable]
public partial record SafeResponse(string Result);

#endregion

#region Test Handlers

public class SimpleCommandHandler : IRequestHandler<SimpleCommand, SimpleResponse>
{
    public Task<CatgaResult<SimpleResponse>> HandleAsync(
        SimpleCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new SimpleResponse($"PROCESSED:{request.Data}");
        return Task.FromResult(CatgaResult<SimpleResponse>.Success(response));
    }
}

public class SimpleEventHandler1 : IEventHandler<SimpleEvent>
{
    public static int ReceivedCount { get; set; }

    public Task HandleAsync(SimpleEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ReceivedCount);
        return Task.CompletedTask;
    }
}

public class SimpleEventHandler2 : IEventHandler<SimpleEvent>
{
    public static int ReceivedCount { get; set; }

    public Task HandleAsync(SimpleEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ReceivedCount);
        return Task.CompletedTask;
    }
}

public class SafeCommandHandler : SafeRequestHandler<SafeCommand, SafeResponse>
{
    protected override Task<SafeResponse> HandleCoreAsync(
        SafeCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Data == "error")
        {
            throw new CatgaException("Simulated error", "TEST_ERROR");
        }

        var response = new SafeResponse($"safe-{request.Data}");
        return Task.FromResult(response);
    }
}

#endregion

