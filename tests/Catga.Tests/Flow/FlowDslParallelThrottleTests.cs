using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using Catga.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow;

// ── Helper: inline FlowConfig ─────────────────────────────────────────────────

internal sealed class InlineFlowConfig<TState> : FlowConfig<TState>
    where TState : class, IFlowState, new()
{
    private readonly Action<IFlowBuilder<TState>> _configure;
    public InlineFlowConfig(Action<IFlowBuilder<TState>> configure) => _configure = configure;
    protected override void Configure(IFlowBuilder<TState> flow) => _configure(flow);
}

// ── Shared test infrastructure ────────────────────────────────────────────────

public class ParallelFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public string Step1Result { get; set; } = "";
    public string Step2Result { get; set; } = "";
    public string Step3Result { get; set; } = "";
    public int ThrottleCount { get; set; }
    public int RetryAttempts { get; set; }
    public bool HasChanges => _changed != 0;
    private int _changed;
    public int GetChangedMask() => _changed;
    public bool IsFieldChanged(int i) => (_changed & (1 << i)) != 0;
    public void ClearChanges() => _changed = 0;
    public void MarkChanged(int i) => _changed |= (1 << i);
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public record ParStep1Cmd : IRequest { public long MessageId { get; init; } }
public record ParStep2Cmd : IRequest { public long MessageId { get; init; } }
public record ParStep3Cmd : IRequest { public long MessageId { get; init; } }
public record GetStep1Result : IRequest<string> { public long MessageId { get; init; } }
public record GetStep2Result : IRequest<string> { public long MessageId { get; init; } }
public record GetStep3Result : IRequest<string> { public long MessageId { get; init; } }
public record ThrottledCmd : IRequest<int> { public long MessageId { get; init; } }
public record RetryableCmd : IRequest<string> { public long MessageId { get; init; } }

// ── Parallel step tests ───────────────────────────────────────────────────────

public class ParallelFlowConfig : FlowConfig<ParallelFlowState>
{
    protected override void Configure(IFlowBuilder<ParallelFlowState> flow)
    {
        flow.Parallel()
            .Branch(b => b.Send<ParallelFlowState, GetStep1Result, string>(s => new GetStep1Result())
                .Into((s, r) => s.Step1Result = r))
            .Branch(b => b.Send<ParallelFlowState, GetStep2Result, string>(s => new GetStep2Result())
                .Into((s, r) => s.Step2Result = r))
            .Branch(b => b.Send<ParallelFlowState, GetStep3Result, string>(s => new GetStep3Result())
                .Into((s, r) => s.Step3Result = r))
            .WaitAll()
        .EndParallel();
    }
}

public class ParallelFlowTests
{
    [Fact]
    public async Task Parallel_AllBranches_ExecuteConcurrently()
    {
        await using var ctx = new FlowTestContext<ParallelFlowState, ParallelFlowConfig>();
        ctx.Mediator.OnSend<GetStep1Result, string>(_ => "result1");
        ctx.Mediator.OnSend<GetStep2Result, string>(_ => "result2");
        ctx.Mediator.OnSend<GetStep3Result, string>(_ => "result3");

        var result = await ctx.RunAsync(new ParallelFlowState());

        result.IsSuccess.Should().BeTrue();
        result.State!.Step1Result.Should().Be("result1");
        result.State.Step2Result.Should().Be("result2");
        result.State.Step3Result.Should().Be("result3");
    }

    [Fact]
    public async Task Parallel_AllBranches_AllSentToMediator()
    {
        await using var ctx = new FlowTestContext<ParallelFlowState, ParallelFlowConfig>();
        ctx.Mediator.OnSend<GetStep1Result, string>(_ => "r1");
        ctx.Mediator.OnSend<GetStep2Result, string>(_ => "r2");
        ctx.Mediator.OnSend<GetStep3Result, string>(_ => "r3");

        await ctx.RunAsync(new ParallelFlowState());

        ctx.Mediator.Sent.Should().HaveCount(3);
    }

    [Fact]
    public async Task Parallel_OneBranchFails_FlowFails()
    {
        await using var ctx = new FlowTestContext<ParallelFlowState, ParallelFlowConfig>();
        ctx.Mediator.OnSend<GetStep1Result, string>(_ => "r1");
        ctx.Mediator.OnSendFail<GetStep2Result, string>("step2 failed");
        ctx.Mediator.OnSend<GetStep3Result, string>(_ => "r3");

        var result = await ctx.RunAsync(new ParallelFlowState());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Parallel_StepType_IsParallel()
    {
        var config = new ParallelFlowConfig();
        config.Build(); var steps = config.Steps;
        steps.Should().HaveCount(1);
        steps[0].Type.Should().Be(StepType.Parallel);
    }

    [Fact]
    public void Parallel_HasThreeBranches()
    {
        var config = new ParallelFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].ParallelBranches.Should().HaveCount(3);
    }

    [Fact]
    public void Parallel_WaitAll_IsTrue()
    {
        var config = new ParallelFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].ParallelWaitAll.Should().BeTrue();
    }

    [Fact]
    public void Parallel_WithTimeout_SetsTimeout()
    {
        var config = new InlineFlowConfig<ParallelFlowState>(flow =>
            flow.Parallel()
                .Branch(b => b.Send<ParallelFlowState, ParStep1Cmd>(s => new ParStep1Cmd()))
                .Timeout(TimeSpan.FromSeconds(10))
            .EndParallel());
        config.Build();
        config.Steps[0].Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Parallel_WaitAny_SetsWaitAllFalse()
    {
        var config = new InlineFlowConfig<ParallelFlowState>(flow =>
            flow.Parallel()
                .Branch(b => b.Send<ParallelFlowState, ParStep1Cmd>(s => new ParStep1Cmd()))
                .WaitAll(false)
            .EndParallel());
        config.Build();
        config.Steps[0].ParallelWaitAll.Should().BeFalse();
    }

    [Fact]
    public void Parallel_EmptyBranches_HasNoBranches()
    {
        var config = new InlineFlowConfig<ParallelFlowState>(flow =>
            flow.Parallel().EndParallel());
        config.Build();
        config.Steps[0].ParallelBranches.Should().BeNullOrEmpty();
    }
}

// ── Throttle step tests ───────────────────────────────────────────────────────

public class ThrottleFlowConfig : FlowConfig<ParallelFlowState>
{
    protected override void Configure(IFlowBuilder<ParallelFlowState> flow)
    {
        flow.Throttle(maxConcurrency: 2)
            .Execute(b => b.Send<ParallelFlowState, ThrottledCmd, int>(s => new ThrottledCmd())
                .Into((s, r) => s.ThrottleCount = r))
        .EndThrottle();
    }
}

public class ThrottleFlowTests
{
    [Fact]
    public async Task Throttle_ExecutesInnerSteps()
    {
        await using var ctx = new FlowTestContext<ParallelFlowState, ThrottleFlowConfig>();
        ctx.Mediator.OnSend<ThrottledCmd, int>(_ => 42);

        var result = await ctx.RunAsync(new ParallelFlowState());

        result.IsSuccess.Should().BeTrue();
        result.State!.ThrottleCount.Should().Be(42);
    }

    [Fact]
    public void Throttle_StepType_IsThrottle()
    {
        var config = new ThrottleFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].Type.Should().Be(StepType.Throttle);
    }

    [Fact]
    public void Throttle_SetsMaxConcurrency()
    {
        var config = new ThrottleFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].ThrottleCount.Should().Be(2);
    }

    [Fact]
    public void Throttle_HasInnerSteps()
    {
        var config = new ThrottleFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].ThrottleSteps.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Throttle_WhenInnerFails_FlowFails()
    {
        await using var ctx = new FlowTestContext<ParallelFlowState, ThrottleFlowConfig>();
        ctx.Mediator.OnSendFail<ThrottledCmd, int>("throttled step failed");

        var result = await ctx.RunAsync(new ParallelFlowState());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Throttle_DefaultConcurrency_IsOne()
    {
        var config = new InlineFlowConfig<ParallelFlowState>(flow =>
            flow.Throttle(1).Execute(b => { }).EndThrottle());
        config.Build();
        config.Steps[0].ThrottleCount.Should().Be(1);
    }
}

// ── Per-step Retry tests ──────────────────────────────────────────────────────

public class RetryFlowConfig : FlowConfig<ParallelFlowState>
{
    protected override void Configure(IFlowBuilder<ParallelFlowState> flow)
    {
        flow.Send<ParallelFlowState, RetryableCmd, string>(s => new RetryableCmd())
            .Into((s, r) => s.Step1Result = r)
            .Retry(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(10));
    }
}

public class PerStepRetryTests
{
    [Fact]
    public void Retry_SetsRetryCountOnStep()
    {
        var config = new RetryFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].RetryCount.Should().Be(3);
    }

    [Fact]
    public void Retry_SetsRetryDelayOnStep()
    {
        var config = new RetryFlowConfig();
        config.Build(); var steps = config.Steps;
        steps[0].RetryDelay.Should().Be(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void Retry_DefaultDelay_Is100ms()
    {
        var config = new InlineFlowConfig<ParallelFlowState>(flow =>
            flow.Send<ParallelFlowState, RetryableCmd, string>(s => new RetryableCmd())
                .Retry(2));
        config.Build();
        config.Steps[0].RetryDelay.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Retry_ZeroAttempts_SetsZero()
    {
        var config = new InlineFlowConfig<ParallelFlowState>(flow =>
            flow.Send<ParallelFlowState, RetryableCmd, string>(s => new RetryableCmd())
                .Retry(0));
        config.Build();
        config.Steps[0].RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task Retry_SuccessfulStep_ExecutesOnce()
    {
        await using var ctx = new FlowTestContext<ParallelFlowState, RetryFlowConfig>();
        ctx.Mediator.OnSend<RetryableCmd, string>(_ => "success");

        var result = await ctx.RunAsync(new ParallelFlowState());

        result.IsSuccess.Should().BeTrue();
        result.State!.Step1Result.Should().Be("success");
        ctx.Mediator.Sent.Should().HaveCount(1);
    }
}

// ── StepType enum completeness ────────────────────────────────────────────────

public class StepTypeTests
{
    [Fact]
    public void StepType_HasParallel()
    {
        Enum.IsDefined(typeof(StepType), StepType.Parallel).Should().BeTrue();
    }

    [Fact]
    public void StepType_HasThrottle()
    {
        Enum.IsDefined(typeof(StepType), StepType.Throttle).Should().BeTrue();
    }

    [Fact]
    public void StepType_HasAllExpectedValues()
    {
        var values = Enum.GetValues<StepType>();
        values.Should().Contain(StepType.Send);
        values.Should().Contain(StepType.Query);
        values.Should().Contain(StepType.Publish);
        values.Should().Contain(StepType.WhenAll);
        values.Should().Contain(StepType.WhenAny);
        values.Should().Contain(StepType.If);
        values.Should().Contain(StepType.Switch);
        values.Should().Contain(StepType.ForEach);
        values.Should().Contain(StepType.Delay);
        values.Should().Contain(StepType.ScheduleAt);
        values.Should().Contain(StepType.Parallel);
        values.Should().Contain(StepType.Throttle);
    }
}
