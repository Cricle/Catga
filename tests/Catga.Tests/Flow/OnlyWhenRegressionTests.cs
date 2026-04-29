using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using Catga.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Regression tests for OnlyWhen bug fix.
/// Bug: StepBuilderBase.OnlyWhen set ConditionFactory but not EvaluateCondition,
/// causing OnlyWhen to never skip steps.
/// Fix: OnlyWhen now sets both ConditionFactory and EvaluateCondition.
/// </summary>
public class OnlyWhenRegressionTests
{
    public record CheckCmd(string Key) : IRequest<string> { public long MessageId { get; init; } }
    public record SkipCmd(string Key) : IRequest<string> { public long MessageId { get; init; } }

    public class OnlyWhenState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool ShouldRun { get; set; }
        public bool ShouldSkip { get; set; }
        public string Result { get; set; } = "";
        public int StepsExecuted { get; set; }
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int i) => false;
        public void ClearChanges() { }
        public void MarkChanged(int i) { }
        public System.Collections.Generic.IEnumerable<string> GetChangedFieldNames() => [];
    }

    // Helper: run a flow config with a mock mediator
    private static async Task<DslFlowResult<OnlyWhenState>> RunFlow<TConfig>(
        TConfig config,
        OnlyWhenState initialState,
        Action<MockMediator>? setupMediator = null)
        where TConfig : FlowConfig<OnlyWhenState>, new()
    {
        var mock = new MockMediator();
        setupMediator?.Invoke(mock);

        var services = new ServiceCollection();
        services.AddSingleton<ICatgaMediator>(mock);
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddSingleton(config);

        var sp = services.BuildServiceProvider();
        var executor = new DslFlowExecutor<OnlyWhenState, TConfig>(
            mock, sp.GetRequiredService<IDslFlowStore>(), config);

        return await executor.RunAsync(initialState);
    }

    // ── Concrete flow configs ─────────────────────────────────────────────────

    public class ConditionFalseFlow : FlowConfig<OnlyWhenState>
    {
        protected override void Configure(IFlowBuilder<OnlyWhenState> flow)
        {
            flow.Send<OnlyWhenState, CheckCmd, string>(s => new CheckCmd("check"))
                .Into((s, r) => { s.Result = r; s.StepsExecuted++; })
                .OnlyWhen(s => s.ShouldRun);
        }
    }

    public class MultiStepFlow : FlowConfig<OnlyWhenState>
    {
        protected override void Configure(IFlowBuilder<OnlyWhenState> flow)
        {
            flow.Send<OnlyWhenState, CheckCmd, string>(s => new CheckCmd("step1"))
                .Into((s, r) => { s.Result += "1"; s.StepsExecuted++; })
                .OnlyWhen(s => s.ShouldRun);

            flow.Send<OnlyWhenState, SkipCmd, string>(s => new SkipCmd("step2"))
                .Into((s, r) => { s.Result += "2"; s.StepsExecuted++; })
                .OnlyWhen(s => s.ShouldSkip);

            flow.Send<OnlyWhenState, CheckCmd, string>(s => new CheckCmd("step3"))
                .Into((s, r) => { s.Result += "3"; s.StepsExecuted++; })
                .OnlyWhen(s => s.ShouldRun);
        }
    }

    public class DynamicConditionFlow : FlowConfig<OnlyWhenState>
    {
        protected override void Configure(IFlowBuilder<OnlyWhenState> flow)
        {
            flow.Send<OnlyWhenState, CheckCmd, string>(s => new CheckCmd("init"))
                .Into((s, r) => s.ShouldRun = r == "run");

            flow.Send<OnlyWhenState, SkipCmd, string>(s => new SkipCmd("conditional"))
                .Into((s, r) => { s.Result = r; s.StepsExecuted++; })
                .OnlyWhen(s => s.ShouldRun);
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyWhen_ConditionFalse_StepIsSkipped()
    {
        var mock = new MockMediator();
        mock.OnSend<CheckCmd, string>(_ => "executed");

        var result = await RunFlow(new ConditionFalseFlow(),
            new OnlyWhenState { ShouldRun = false },
            m => m.OnSend<CheckCmd, string>(_ => "executed"));

        result.IsSuccess.Should().BeTrue();
        result.State!.StepsExecuted.Should().Be(0, "step should be skipped when OnlyWhen=false");
        result.State.Result.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyWhen_ConditionTrue_StepExecutes()
    {
        var result = await RunFlow(new ConditionFalseFlow(),
            new OnlyWhenState { ShouldRun = true },
            m => m.OnSend<CheckCmd, string>(_ => "executed"));

        result.IsSuccess.Should().BeTrue();
        result.State!.StepsExecuted.Should().Be(1);
        result.State.Result.Should().Be("executed");
    }

    [Fact]
    public async Task OnlyWhen_MultipleSteps_OnlyConditionTrueStepsRun()
    {
        var mock = new MockMediator();
        mock.OnSend<CheckCmd, string>(_ => "ok");
        mock.OnSend<SkipCmd, string>(_ => "should-not-run");

        var result = await RunFlow(new MultiStepFlow(),
            new OnlyWhenState { ShouldRun = true, ShouldSkip = false },
            m => { m.OnSend<CheckCmd, string>(_ => "ok"); m.OnSend<SkipCmd, string>(_ => "bad"); });

        result.IsSuccess.Should().BeTrue();
        result.State!.StepsExecuted.Should().Be(2, "only 2 of 3 steps should run");
        result.State.Result.Should().Be("13");
    }

    [Fact]
    public async Task OnlyWhen_AllConditionsFalse_FlowSucceedsWithNoSteps()
    {
        // Use ConditionFalseFlow with ShouldRun=false
        var result = await RunFlow(new ConditionFalseFlow(),
            new OnlyWhenState { ShouldRun = false });

        result.IsSuccess.Should().BeTrue();
        result.State!.StepsExecuted.Should().Be(0);
    }

    [Fact]
    public async Task OnlyWhen_DynamicCondition_BasedOnPreviousStepResult()
    {
        var result = await RunFlow(new DynamicConditionFlow(),
            new OnlyWhenState(),
            m => { m.OnSend<CheckCmd, string>(_ => "run"); m.OnSend<SkipCmd, string>(_ => "done"); });

        result.IsSuccess.Should().BeTrue();
        result.State!.StepsExecuted.Should().Be(1);
        result.State.Result.Should().Be("done");
    }

    // ── Unit tests of the fix itself ──────────────────────────────────────────

    [Fact]
    public void OnlyWhen_SetsEvaluateCondition_NotNull()
    {
        var config = new ConditionFalseFlow();
        config.Build();

        var step = config.Steps[0];
        step.HasCondition.Should().BeTrue();
        step.EvaluateCondition.Should().NotBeNull("EvaluateCondition must be set by OnlyWhen");
    }

    [Fact]
    public void OnlyWhen_EvaluateCondition_ReturnsCorrectValue()
    {
        var config = new ConditionFalseFlow();
        config.Build();

        var step = config.Steps[0];
        step.EvaluateCondition!(new OnlyWhenState { ShouldRun = true }).Should().BeTrue();
        step.EvaluateCondition!(new OnlyWhenState { ShouldRun = false }).Should().BeFalse();
    }

    [Fact]
    public void OnlyWhen_ConditionFactory_AlsoSet()
    {
        var config = new ConditionFalseFlow();
        config.Build();

        config.Steps[0].ConditionFactory.Should().NotBeNull();
    }
}
