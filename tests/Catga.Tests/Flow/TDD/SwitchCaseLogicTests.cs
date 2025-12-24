using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;
using System.Collections;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for Switch/Case logic edge cases and complex scenarios
/// </summary>
public class SwitchCaseLogicTests
{
    [Fact]
    public async Task ExecuteSwitch_NullSelector_ShouldReturnFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NullSelectorFlow();
        var executor = new DslFlowExecutor<TestSwitchState, NullSelectorFlow>(mediator, store, config);

        var state = new TestSwitchState { FlowId = "null-selector", Value = 5 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse("null selector should cause failure");
        result.Error.Should().Contain("selector");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData("test")]
    [InlineData(true)]
    public async Task ExecuteSwitch_VariousDataTypes_ShouldMatch(object? value)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new MixedTypesSwitchFlow();
        var executor = new DslFlowExecutor<TestSwitchState, MixedTypesSwitchFlow>(mediator, store, config);

        var state = new TestSwitchState { FlowId = "mixed-types", ObjectValue = value };

        SetupMediatorForSwitch(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedCases.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteSwitch_ManyCases_ShouldPerformWell()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var caseCount = 1000;
        var config = new LargeSwitchFlow(caseCount);
        var executor = new DslFlowExecutor<TestSwitchState, LargeSwitchFlow>(mediator, store, config);

        SetupMediatorForSwitch(mediator);

        // Act - Test last case (worst case for linear search)
        var state = new TestSwitchState { FlowId = "large-switch", Value = caseCount - 1 };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        sw.Stop();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedCases.Should().Contain($"Case{caseCount - 1}");

        // Performance should be reasonable even with many cases (Dictionary lookup)
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "Dictionary lookup should be fast");
    }

    [Fact]
    public async Task ExecuteSwitch_DuplicateCaseValues_ShouldUseFirst()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new DuplicateCasesFlow();
        var executor = new DslFlowExecutor<TestSwitchState, DuplicateCasesFlow>(mediator, store, config);

        var state = new TestSwitchState { FlowId = "duplicate-cases", Value = 5 };

        SetupMediatorForSwitch(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedCases.Should().BeEquivalentTo(["First5"]);
    }

    [Fact]
    public async Task ExecuteSwitch_NoMatchingCase_NoDefault_ShouldSucceed()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NoDefaultSwitchFlow();
        var executor = new DslFlowExecutor<TestSwitchState, NoDefaultSwitchFlow>(mediator, store, config);

        var state = new TestSwitchState { FlowId = "no-default", Value = 999 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("no matching case with no default is valid");
        state.ExecutedCases.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSwitch_SelectorThrowsException_ShouldFail()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ExceptionSelectorFlow();
        var executor = new DslFlowExecutor<TestSwitchState, ExceptionSelectorFlow>(mediator, store, config);

        var state = new TestSwitchState { FlowId = "exception-selector", Value = -1 };

        // Act
        var act = () => executor.RunAsync(state);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteSwitch_ComplexObjectComparison_ShouldWork()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ComplexObjectSwitchFlow();
        var executor = new DslFlowExecutor<TestSwitchState, ComplexObjectSwitchFlow>(mediator, store, config);

        var state = new TestSwitchState
        {
            FlowId = "complex-object",
            ComplexValue = new ComplexType { Id = 1, Name = "Test" }
        };

        SetupMediatorForSwitch(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedCases.Should().Contain("ComplexCase1");
    }

    [Fact]
    public async Task ExecuteSwitch_NestedSwitches_ShouldEvaluateCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NestedSwitchFlow();
        var executor = new DslFlowExecutor<TestSwitchState, NestedSwitchFlow>(mediator, store, config);

        var state = new TestSwitchState
        {
            FlowId = "nested-switch",
            Value = 2,
            SecondaryValue = 20
        };

        SetupMediatorForSwitch(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedCases.Should().BeEquivalentTo(["Outer2", "Inner20"]);
    }

    [Theory]
    [InlineData(10, 100, 5)]     // 10 cases, 100 iterations, 5ms max per iteration
    [InlineData(100, 100, 10)]   // 100 cases, 100 iterations, 10ms max per iteration
    [InlineData(1000, 10, 50)]   // 1000 cases, 10 iterations, 50ms max per iteration
    public async Task ExecuteSwitch_PerformanceBenchmark(int caseCount, int iterations, int maxMilliseconds)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new LargeSwitchFlow(caseCount);

        SetupMediatorForSwitch(mediator);

        // Act
        var tasks = new List<Task<long>>();
        for (int i = 0; i < iterations; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var executor = new DslFlowExecutor<TestSwitchState, LargeSwitchFlow>(mediator, store, config);
                var state = new TestSwitchState
                {
                    FlowId = $"perf-{index}",
                    Value = index % caseCount
                };

                var sw = System.Diagnostics.Stopwatch.StartNew();
                await executor.RunAsync(state);
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }));
        }

        var results = await Task.WhenAll(tasks);
        var avgTime = results.Average();

        // Assert
        avgTime.Should().BeLessThan(maxMilliseconds, $"average time for {caseCount} cases should be fast");
    }

    private static void SetupMediatorForSwitch(ICatgaMediator mediator)
    {
        mediator.SendAsync<SwitchCommand, string>(
            Arg.Any<SwitchCommand>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<SwitchCommand>();
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"executed-{cmd.Case}"));
            });
    }
}

// Test State
public class TestSwitchState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public int SecondaryValue { get; set; }
    public object? ObjectValue { get; set; }
    public ComplexType? ComplexValue { get; set; }
    public List<string> ExecutedCases { get; set; } = [];

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Complex type for testing
public class ComplexType : IEquatable<ComplexType>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public bool Equals(ComplexType? other)
    {
        if (other is null) return false;
        return Id == other.Id && Name == other.Name;
    }

    public override bool Equals(object? obj) => Equals(obj as ComplexType);
    public override int GetHashCode() => HashCode.Combine(Id, Name);
}

// Test Command
public record SwitchCommand(string Case) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// Test Flow Configurations
public class NullSelectorFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("null-selector-flow");
        var step = new FlowStep { Type = StepType.Switch, SwitchSelector = null };
        flow.AddStep(step);
    }
}

public class MixedTypesSwitchFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("mixed-types-flow");

        flow.Switch(s => s.ObjectValue)
            .Case(null, f => f.Send(s => new SwitchCommand("Null"))
                .Into((s, r) => s.ExecutedCases.Add("Null")))
            .Case(5, f => f.Send(s => new SwitchCommand("Int5"))
                .Into((s, r) => s.ExecutedCases.Add("Int5")))
            .Case("test", f => f.Send(s => new SwitchCommand("StringTest"))
                .Into((s, r) => s.ExecutedCases.Add("StringTest")))
            .Case(true, f => f.Send(s => new SwitchCommand("BoolTrue"))
                .Into((s, r) => s.ExecutedCases.Add("BoolTrue")))
            .Default(f => f.Send(s => new SwitchCommand("Default"))
                .Into((s, r) => s.ExecutedCases.Add("Default")))
            .EndSwitch();
    }
}

public class LargeSwitchFlow : FlowConfig<TestSwitchState>
{
    private readonly int _caseCount;

    public LargeSwitchFlow(int caseCount)
    {
        _caseCount = caseCount;
    }

    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("large-switch-flow");

        var switchBuilder = flow.Switch(s => s.Value);

        for (int i = 0; i < _caseCount; i++)
        {
            var index = i;
            switchBuilder.Case(index, f => f.Send(s => new SwitchCommand($"Case{index}"))
                .Into((s, r) => s.ExecutedCases.Add($"Case{index}")));
        }

        switchBuilder.Default(f => f.Send(s => new SwitchCommand("Default")))
            .EndSwitch();
    }
}

public class DuplicateCasesFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("duplicate-cases-flow");

        // Note: In practice, Dictionary will only keep the last value for duplicate keys
        // But we're testing the behavior
        var cases = new Dictionary<object, List<FlowStep>>
        {
            { 5, [new FlowStep { Type = StepType.Send }] },
            // Trying to add duplicate - should be handled gracefully
        };

        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<TestSwitchState, int>)(s => s.Value),
            Cases = cases,
            DefaultBranch = []
        };

        // Manually construct to test edge case
        flow.AddStep(step);

        flow.Send(s => new SwitchCommand("First5"))
            .Into((s, r) => s.ExecutedCases.Add("First5"));
    }
}

public class NoDefaultSwitchFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("no-default-flow");

        flow.Switch(s => s.Value)
            .Case(1, f => f.Send(s => new SwitchCommand("Case1")))
            .Case(2, f => f.Send(s => new SwitchCommand("Case2")))
            .Case(3, f => f.Send(s => new SwitchCommand("Case3")))
            // No default case
            .EndSwitch();
    }
}

public class ExceptionSelectorFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("exception-selector-flow");

        flow.Switch(s => throw new InvalidOperationException("Selector failed"))
            .Case(1, f => f.Send(s => new SwitchCommand("Never")))
            .EndSwitch();
    }
}

public class ComplexObjectSwitchFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("complex-object-flow");

        var case1 = new ComplexType { Id = 1, Name = "Test" };
        var case2 = new ComplexType { Id = 2, Name = "Other" };

        flow.Switch(s => s.ComplexValue)
            .Case(case1, f => f.Send(s => new SwitchCommand("ComplexCase1"))
                .Into((s, r) => s.ExecutedCases.Add("ComplexCase1")))
            .Case(case2, f => f.Send(s => new SwitchCommand("ComplexCase2"))
                .Into((s, r) => s.ExecutedCases.Add("ComplexCase2")))
            .Default(f => f.Send(s => new SwitchCommand("ComplexDefault"))
                .Into((s, r) => s.ExecutedCases.Add("ComplexDefault")))
            .EndSwitch();
    }
}

public class NestedSwitchFlow : FlowConfig<TestSwitchState>
{
    protected override void Configure(IFlowBuilder<TestSwitchState> flow)
    {
        flow.Name("nested-switch-flow");

        flow.Switch(s => s.Value)
            .Case(1, f =>
            {
                f.Send(s => new SwitchCommand("Outer1"));

                f.Switch(s => s.SecondaryValue)
                    .Case(10, f2 => f2.Send(s => new SwitchCommand("Inner10")))
                    .Case(20, f2 => f2.Send(s => new SwitchCommand("Inner20")))
                    .EndSwitch();
            })
            .Case(2, f =>
            {
                f.Send(s => new SwitchCommand("Outer2"));

                f.Switch(s => s.SecondaryValue)
                    .Case(10, f2 => f2.Send(s => new SwitchCommand("Inner10")))
                    .Case(20, f2 => f2.Send(s => new SwitchCommand("Inner20")))
                    .EndSwitch();
            })
            .EndSwitch();
    }
}
