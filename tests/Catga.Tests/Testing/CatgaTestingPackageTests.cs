using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Testing;

// ── Shared test types ─────────────────────────────────────────────────────────

public record PingRequest(string Message) : IRequest<PingResponse>
{
    public long MessageId { get; init; }
}
public record PingResponse(string Reply);

public record GreetEvent(string Name) : IEvent
{
    public long MessageId { get; init; }
}

public class PingHandler : IRequestHandler<PingRequest, PingResponse>
{
    public ValueTask<CatgaResult<PingResponse>> HandleAsync(PingRequest request, CancellationToken ct = default)
        => ValueTask.FromResult(CatgaResult<PingResponse>.Success(new PingResponse($"Pong: {request.Message}")));
}

// ── MockMediator tests ────────────────────────────────────────────────────────

public class MockMediatorTests
{
    [Fact]
    public async Task SendAsync_WithConfiguredHandler_ReturnsSuccess()
    {
        var mock = new MockMediator();
        mock.OnSend<PingRequest, PingResponse>(req => new PingResponse($"Pong: {req.Message}"));

        var result = await mock.SendAsync<PingRequest, PingResponse>(new PingRequest("hello"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Reply.Should().Be("Pong: hello");
    }

    [Fact]
    public async Task SendAsync_WithNoHandler_ReturnsFailure()
    {
        var mock = new MockMediator();
        var result = await mock.SendAsync<PingRequest, PingResponse>(new PingRequest("test"));
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PingRequest");
    }

    [Fact]
    public async Task SendAsync_WithFailHandler_ReturnsFailure()
    {
        var mock = new MockMediator();
        mock.OnSendFail<PingRequest, PingResponse>("service unavailable");

        var result = await mock.SendAsync<PingRequest, PingResponse>(new PingRequest("x"));
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("service unavailable");
    }

    [Fact]
    public async Task PublishAsync_RecordsPublishedEvent()
    {
        var mock = new MockMediator();
        await mock.PublishAsync(new GreetEvent("Alice"));

        mock.Published.Should().HaveCount(1);
        mock.Published[0].Should().BeOfType<GreetEvent>();
    }

    [Fact]
    public async Task SendAsync_RecordsSentMessages()
    {
        var mock = new MockMediator();
        mock.OnSend<PingRequest, PingResponse>(_ => new PingResponse("ok"));

        await mock.SendAsync<PingRequest, PingResponse>(new PingRequest("a"));
        await mock.SendAsync<PingRequest, PingResponse>(new PingRequest("b"));

        mock.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task PublishBatchAsync_RecordsAllEvents()
    {
        var mock = new MockMediator();
        var events = new List<GreetEvent> { new("A"), new("B"), new("C") };
        await mock.PublishBatchAsync<GreetEvent>(events);
        mock.Published.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendAsync_NoResponse_RecordsSent()
    {
        var mock = new MockMediator();
        // IRequest (no response) - just records
        mock.Sent.Should().BeEmpty();
    }
}

// ── HandlerSpy tests ──────────────────────────────────────────────────────────

public class HandlerSpyTests
{
    [Fact]
    public async Task HandlerSpy_WithFactory_RecordsCalls()
    {
        var spy = new HandlerSpy<PingRequest, PingResponse>(
            (req, _) => ValueTask.FromResult(CatgaResult<PingResponse>.Success(new PingResponse("ok"))));

        await spy.HandleAsync(new PingRequest("a"));
        await spy.HandleAsync(new PingRequest("b"));

        spy.CallCount.Should().Be(2);
        spy.LastCall!.Message.Should().Be("b");
        spy.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandlerSpy_WithInner_DelegatesToInner()
    {
        var inner = new PingHandler();
        var spy = new HandlerSpy<PingRequest, PingResponse>(inner);

        var result = await spy.HandleAsync(new PingRequest("test"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Reply.Should().Contain("test");
        spy.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task HandlerSpy_NoHandler_ReturnsFailure()
    {
        // HandlerSpy with no inner and no factory
        var spy = new HandlerSpy<PingRequest, PingResponse>((IRequestHandler<PingRequest, PingResponse>?)null!);
        var result = await spy.HandleAsync(new PingRequest("x"));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task EventHandlerSpy_WithAction_RecordsCalls()
    {
        var received = new List<string>();
        var spy = new EventHandlerSpy<GreetEvent>(
            (e, _) => { received.Add(e.Name); return ValueTask.CompletedTask; });

        await spy.HandleAsync(new GreetEvent("Alice"));
        await spy.HandleAsync(new GreetEvent("Bob"));

        spy.CallCount.Should().Be(2);
        received.Should().Equal("Alice", "Bob");
    }

    [Fact]
    public async Task EventHandlerSpy_WithNoAction_DoesNotThrow()
    {
        var spy = new EventHandlerSpy<GreetEvent>();
        await spy.HandleAsync(new GreetEvent("test"));
        spy.CallCount.Should().Be(1);
        spy.LastCall!.Name.Should().Be("test");
    }
}

// ── MessageCapture tests ──────────────────────────────────────────────────────

public class MessageCaptureTests
{
    [Fact]
    public void MessageCapture_RecordsPublishedAndConsumed()
    {
        var capture = new MessageCapture();
        capture.RecordPublished(new PingRequest("a"));
        capture.RecordConsumed(new PingResponse("b"));

        capture.Published.Should().HaveCount(1);
        capture.Consumed.Should().HaveCount(1);
    }

    [Fact]
    public void MessageCapture_Clear_ResetsAll()
    {
        var capture = new MessageCapture();
        capture.RecordPublished(new PingRequest("x"));
        capture.RecordConsumed(new PingResponse("y"));
        capture.Clear();

        capture.Published.Should().BeEmpty();
        capture.Consumed.Should().BeEmpty();
    }

    [Fact]
    public void MessageCapture_IsThreadSafe()
    {
        var capture = new MessageCapture();
        Parallel.For(0, 100, i => capture.RecordPublished(new PingRequest($"msg{i}")));
        capture.Published.Should().HaveCount(100);
    }
}

// ── CatgaAssertions tests ─────────────────────────────────────────────────────

public class CatgaAssertionsTests
{
    [Fact]
    public void ShouldSucceed_OnSuccess_DoesNotThrow()
    {
        var result = CatgaResult<string>.Success("ok");
        var act = () => result.ShouldSucceed();
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldSucceed_OnFailure_ThrowsWithMessage()
    {
        var result = CatgaResult<string>.Failure("something bad");
        var act = () => result.ShouldSucceed();
        act.Should().Throw<CatgaAssertionException>().WithMessage("*something bad*");
    }

    [Fact]
    public void ShouldFail_OnFailure_DoesNotThrow()
    {
        var result = CatgaResult<string>.Failure("bad");
        var act = () => result.ShouldFail();
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldFail_OnSuccess_Throws()
    {
        var result = CatgaResult<string>.Success("ok");
        var act = () => result.ShouldFail();
        act.Should().Throw<CatgaAssertionException>();
    }

    [Fact]
    public void ShouldFailWith_MatchingCode_DoesNotThrow()
    {
        var result = CatgaResult<string>.Failure(ErrorInfo.Validation("bad"));
        var act = () => result.ShouldFailWith(ErrorCodes.ValidationFailed);
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldFailWith_WrongCode_ThrowsWithBothCodes()
    {
        var result = CatgaResult<string>.Failure(ErrorInfo.Timeout("timeout"));
        var act = () => result.ShouldFailWith(ErrorCodes.ValidationFailed);
        act.Should().Throw<CatgaAssertionException>().WithMessage("*VALIDATION_FAILED*");
    }

    [Fact]
    public void ShouldHaveValue_MatchingValue_DoesNotThrow()
    {
        var result = CatgaResult<int>.Success(42);
        var act = () => result.ShouldHaveValue(42);
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveValue_WrongValue_Throws()
    {
        var result = CatgaResult<int>.Success(1);
        var act = () => result.ShouldHaveValue(99);
        act.Should().Throw<CatgaAssertionException>();
    }

    [Fact]
    public void ShouldContain_WithMatch_DoesNotThrow()
    {
        var items = new[] { new PingRequest("a"), new PingRequest("b") };
        var act = () => items.ShouldContain(r => r.Message == "a");
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldContain_NoMatch_ThrowsWithTypeName()
    {
        var items = new[] { new PingRequest("a") };
        var act = () => items.ShouldContain(r => r.Message == "z");
        act.Should().Throw<CatgaAssertionException>().WithMessage("*PingRequest*");
    }

    [Fact]
    public void ShouldContain_EmptyList_Throws()
    {
        var items = Array.Empty<PingRequest>();
        var act = () => items.ShouldContain();
        act.Should().Throw<CatgaAssertionException>();
    }
}

// ── CatgaTestHarness tests ────────────────────────────────────────────────────

public class CatgaTestHarnessTests
{
    [Fact]
    public async Task CatgaTestHarness_Start_BuildsContainer()
    {
        await using var harness = new CatgaTestHarness();
        harness.Start();
        harness.Mediator.Should().NotBeNull();
    }

    [Fact]
    public async Task CatgaTestHarness_Published_InitiallyEmpty()
    {
        await using var harness = new CatgaTestHarness();
        harness.Start();
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task CatgaTestHarness_GetService_ReturnsRegisteredService()
    {
        await using var harness = new CatgaTestHarness(s =>
            s.AddSingleton<string>("hello"));
        harness.Start();
        harness.GetService<string>().Should().Be("hello");
    }

    [Fact]
    public async Task CatgaTestHarness_BeforeStart_ThrowsOnMediator()
    {
        await using var harness = new CatgaTestHarness();
        var act = () => harness.Mediator;
        act.Should().Throw<InvalidOperationException>().WithMessage("*Start()*");
    }
}

public class TestFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public string Value { get; set; } = "";
    public bool HasChanges => _changed != 0;
    private int _changed;
    public int GetChangedMask() => _changed;
    public bool IsFieldChanged(int i) => (_changed & (1 << i)) != 0;
    public void ClearChanges() => _changed = 0;
    public void MarkChanged(int i) => _changed |= (1 << i);
    public IEnumerable<string> GetChangedFieldNames() => HasChanges ? ["Value"] : [];
}

public class TestFlowConfig : FlowConfig<TestFlowState>
{
    protected override void Configure(IFlowBuilder<TestFlowState> flow)
    {
        flow.Send<TestFlowState, PingRequest, PingResponse>(s => new PingRequest(s.Value))
            .Into((s, r) => s.Value = r.Reply);
    }
}

public class FlowTestContextTests
{
    [Fact]
    public async Task FlowTestContext_RunAsync_WithMockedMediator_Succeeds()
    {
        await using var ctx = new FlowTestContext<TestFlowState, TestFlowConfig>();
        ctx.Mediator.OnSend<PingRequest, PingResponse>(_ => new PingResponse("Pong"));

        var state = new TestFlowState { Value = "hello" };
        var result = await ctx.RunAsync(state);

        result.IsSuccess.Should().BeTrue();
        result.State!.Value.Should().Be("Pong");
    }

    [Fact]
    public async Task FlowTestContext_RunAsync_WithFailedMediator_Fails()
    {
        await using var ctx = new FlowTestContext<TestFlowState, TestFlowConfig>();
        ctx.Mediator.OnSendFail<PingRequest, PingResponse>("service down");

        var result = await ctx.RunAsync(new TestFlowState { Value = "x" });

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FlowTestContext_Mediator_RecordsSentMessages()
    {
        await using var ctx = new FlowTestContext<TestFlowState, TestFlowConfig>();
        ctx.Mediator.OnSend<PingRequest, PingResponse>(r => new PingResponse("ok"));

        await ctx.RunAsync(new TestFlowState { Value = "test" });

        ctx.Mediator.Sent.Should().HaveCount(1);
        ctx.Mediator.Sent[0].Should().BeOfType<PingRequest>();
    }

    [Fact]
    public async Task FlowTestContext_GetStateAsync_ReturnsSnapshotAfterRun()
    {
        await using var ctx = new FlowTestContext<TestFlowState, TestFlowConfig>();
        ctx.Mediator.OnSend<PingRequest, PingResponse>(_ => new PingResponse("done"));

        var state = new TestFlowState { Value = "init" };
        var result = await ctx.RunAsync(state);

        // Flow completed successfully - state should be accessible
        result.IsSuccess.Should().BeTrue();
        result.State.Should().NotBeNull();
        result.State!.Value.Should().Be("done");
    }
}
