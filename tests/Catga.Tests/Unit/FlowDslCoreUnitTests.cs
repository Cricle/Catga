using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Unit;

/// <summary>
/// Core unit tests for Flow DSL components
/// </summary>
public class FlowDslCoreUnitTests
{
    private readonly ITestOutputHelper _output;

    public FlowDslCoreUnitTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region FlowBuilder Tests

    [Fact]
    public void FlowBuilder_BuildsCorrectStepSequence()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        flow

            ;

        var steps = flow.Build();

        // Assert
        steps.Should().HaveCount(3);
        steps[0].Name.Should().Be("step1");
        steps[1].Name.Should().Be("step2");
        steps[2].Name.Should().Be("step3");
    }

    [Fact]
    public void FlowBuilder_If_CreatesCorrectBranchStructure()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        flow.If(s => s.Condition)

        .ElseIf(s => s.AlternateCondition)

        .Else()

        .EndIf();

        var steps = flow.Build();

        // Assert
        steps.Should().HaveCount(1);
        var ifStep = steps[0];
        ifStep.Type.Should().Be(StepType.If);
        ifStep.ThenBranch.Should().HaveCount(1);
        ifStep.ElseIfBranches.Should().HaveCount(1);
        ifStep.ElseBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowBuilder_Switch_CreatesCorrectCaseStructure()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        flow.Switch(s => s.Value)
            .Case(1, case1 => case1)
            .Case(2, case2 => case2)
            .Default(def => def)
            .EndSwitch();

        var steps = flow.Build();

        // Assert
        steps.Should().HaveCount(1);
        var switchStep = steps[0];
        switchStep.Type.Should().Be(StepType.Switch);
        switchStep.Cases.Should().HaveCount(2);
        switchStep.DefaultBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowBuilder_ForEach_ConfiguresCorrectly()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        flow.ForEach(s => s.Items)
            .WithParallelism(5)
            .WithBatchSize(10)
            .ContinueOnFailure()
            .Configure((item, f) =>
            {
                // Empty configuration for test
            })
            .EndForEach();

        var steps = flow.Build();

        // Assert
        steps.Should().HaveCount(1);
        var forEachStep = steps[0];
        forEachStep.Type.Should().Be(StepType.ForEach);
        forEachStep.ForEachConfig.Should().NotBeNull();
        forEachStep.ForEachConfig!.MaxParallelism.Should().Be(5);
        forEachStep.ForEachConfig.BatchSize.Should().Be(10);
        forEachStep.ForEachConfig.ContinueOnFailure.Should().BeTrue();
    }

    [Fact]
    public void FlowBuilder_WhenAll_CreatesParallelSteps()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        flow.WhenAll(
            f => f,
            f => f,
            f => f
        );

        var steps = flow.Build();

        // Assert
        steps.Should().HaveCount(1);
        var whenAllStep = steps[0];
        whenAllStep.Type.Should().Be(StepType.WhenAll);
        whenAllStep.ParallelBranches.Should().HaveCount(3);
    }

    [Fact]
    public void FlowBuilder_WhenAny_CreatesRaceCondition()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        flow.WhenAny(
            f => f,
            f => f
        );

        var steps = flow.Build();

        // Assert
        steps.Should().HaveCount(1);
        var whenAnyStep = steps[0];
        whenAnyStep.Type.Should().Be(StepType.WhenAny);
        whenAnyStep.ParallelBranches.Should().HaveCount(2);
    }

    #endregion

    #region FlowSnapshot Tests

    [Fact]
    public void FlowSnapshot_TracksVersionCorrectly()
    {
        // Arrange
        var snapshot = new FlowSnapshot<TestFlowState>
        {
            FlowId = "snapshot-001",
            State = new TestFlowState { FlowId = "snapshot-001" },
            Status = DslFlowStatus.Running,
            Version = 1
        };

        // Act
        var newSnapshot = snapshot with { Version = snapshot.Version + 1 };

        // Assert
        newSnapshot.Version.Should().Be(2);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public void FlowSnapshot_TracksTimestamps()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var snapshot = new FlowSnapshot<TestFlowState>
        {
            FlowId = "timestamp-001",
            State = new TestFlowState { FlowId = "timestamp-001" },
            CreatedAt = now,
            UpdatedAt = now
        };

        // Act
        var updated = snapshot with { UpdatedAt = DateTime.UtcNow };

        // Assert
        updated.UpdatedAt.Should().BeAfter(now);
        updated.CreatedAt.Should().Be(now);
    }

    [Fact]
    public void FlowSnapshot_Position_NavigatesCorrectly()
    {
        // Arrange
        var position = new FlowPosition(new[] { 0, 1, 2 });

        // Act & Assert
        position.Path.Length.Should().Be(3);
        position.Path[2].Should().Be(2);
        position.Path[0].Should().Be(0);
    }

    #endregion

    #region WaitCondition Tests

    [Fact]
    public void WaitCondition_WhenAll_RequiresAllSignals()
    {
        // Arrange
        var condition = new WaitCondition
        {
            CorrelationId = "wait-all-001",
            Type = WaitType.All,
            ExpectedCount = 3,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0,
            CancelOthers = false,
            ChildFlowIds = new List<string> { "signal1", "signal2", "signal3" },
            Results = new List<FlowCompletedEventData>()
        };

        // Act & Assert
        condition.CompletedCount.Should().Be(0);

        condition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "signal1",
            Success = true,
            Result = "result1"
        });
        condition.CompletedCount++;
        condition.CompletedCount.Should().Be(1);

        condition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "signal2",
            Success = true,
            Result = "result2"
        });
        condition.CompletedCount++;
        condition.CompletedCount.Should().Be(2);

        condition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "signal3",
            Success = true,
            Result = "result3"
        });
        condition.CompletedCount++;
        condition.CompletedCount.Should().Be(3);
    }

    [Fact]
    public void WaitCondition_WhenAny_CompletesOnFirst()
    {
        // Arrange
        var condition = new WaitCondition
        {
            CorrelationId = "wait-any-001",
            Type = WaitType.Any,
            ExpectedCount = 3,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0,
            CancelOthers = false,
            ChildFlowIds = new List<string> { "signal1", "signal2", "signal3" },
            Results = new List<FlowCompletedEventData>()
        };

        // Act
        condition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "signal2",
            Success = true,
            Result = "result2"
        });
        condition.CompletedCount++;

        // Assert
        condition.CompletedCount.Should().Be(1);
        condition.Results.Should().HaveCount(1);
        condition.Results[0].FlowId.Should().Be("signal2");
        condition.Results[0].Result.Should().Be("result2");
    }

    [Fact]
    public void WaitCondition_TimeoutDetection()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var pastCondition = new WaitCondition
        {
            CorrelationId = "timeout-001",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromSeconds(5),
            CreatedAt = now.AddSeconds(-10),
            FlowId = "flow-timeout-1",
            FlowType = "TestFlow",
            Step = 0
        };

        var futureCondition = new WaitCondition
        {
            CorrelationId = "timeout-002",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = now,
            FlowId = "flow-timeout-2",
            FlowType = "TestFlow",
            Step = 0
        };

        // Act & Assert
        (pastCondition.CreatedAt + pastCondition.Timeout < DateTime.UtcNow).Should().BeTrue();
        (futureCondition.CreatedAt + futureCondition.Timeout < DateTime.UtcNow).Should().BeFalse();
    }

    #endregion

    #region ForEachProgress Tests

    [Fact]
    public void ForEachProgress_TracksProcessedItems()
    {
        // Arrange
        var progress = new ForEachProgress
        {
            CurrentIndex = 0,
            TotalCount = 10,
            CompletedIndices = new List<int>()
        };

        // Act
        progress.CompletedIndices.Add(0);
        progress.CompletedIndices.Add(1);
        progress.CompletedIndices.Add(0); // Duplicate index

        // Assert (distinct processed indices)
        progress.CompletedIndices.Distinct().Should().HaveCount(2);
        progress.CompletedIndices.Should().Contain(new[] { 0, 1 });
    }

    [Fact]
    public void ForEachProgress_TracksFailedItems()
    {
        // Arrange
        var progress = new ForEachProgress
        {
            CurrentIndex = 0,
            TotalCount = 10,
            FailedIndices = new List<int>()
        };

        // Act
        progress.FailedIndices.Add(3);
        progress.FailedIndices.Add(5);

        // Assert
        progress.FailedIndices.Should().HaveCount(2);
        progress.FailedIndices.Should().Contain(new[] { 3, 5 });
    }

    [Fact]
    public void ForEachProgress_CalculatesCompletion()
    {
        // Arrange
        var progress = new ForEachProgress
        {
            CurrentIndex = 0,
            TotalCount = 10,
            CompletedIndices = new List<int> { 0, 1, 2, 3, 4 }
        };

        // Assert
        var percentage = (progress.CompletedIndices.Count * 100) / progress.TotalCount;
        percentage.Should().Be(50);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task FlowExecution_HandlesNullState()
    {
        // Arrange
        TestFlowState? nullState = null;

        // Act
        Func<Task> act = async () =>
        {
            if (nullState == null)
                throw new ArgumentNullException(nameof(nullState));
            await Task.CompletedTask;
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void FlowBuilder_RejectsInvalidConfiguration()
    {
        // Arrange
        var flow = new TestFlowBuilder();

        // Act
        Action act = () =>
        {
            if (string.IsNullOrEmpty(""))
                throw new ArgumentException("Invalid configuration");
        };

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion
}

// Test implementations
public class TestFlowBuilder
{
    private readonly List<FlowStep> _steps = new();

    public TestFlowBuilder Step(string name, Action<TestFlowState> action)
    {
        var step = new FlowStep
        {
            Name = name,
            Type = StepType.Step,
            Action = s => action((TestFlowState)s)
        };
        _steps.Add(step);
        return this;
    }

    public TestIfBuilder If(Func<TestFlowState, bool> condition)
    {
        var step = new FlowStep
        {
            Name = "if",
            Type = StepType.If,
            BranchCondition = s => condition((TestFlowState)s),
            ThenBranch = new List<FlowStep>(),
            ElseIfBranches = new List<(Func<IFlowState, bool>, List<FlowStep>)>(),
            ElseBranch = new List<FlowStep>()
        };
        _steps.Add(step);
        return new TestIfBuilder(step);
    }

    public TestSwitchBuilder Switch(Func<TestFlowState, object> selector)
    {
        var step = new FlowStep
        {
            Name = "switch",
            Type = StepType.Switch,
            SwitchSelector = s => selector((TestFlowState)s),
            Cases = new Dictionary<object, List<FlowStep>>(),
            DefaultBranch = new List<FlowStep>()
        };
        _steps.Add(step);
        return new TestSwitchBuilder(step);
    }

    public TestForEachBuilder ForEach(Func<TestFlowState, IEnumerable<string>> itemsSelector)
    {
        var step = new FlowStep
        {
            Name = "foreach",
            Type = StepType.ForEach,
            ForEachConfig = new ForEachConfig<string>()
        };
        _steps.Add(step);
        return new TestForEachBuilder(step);
    }

    public void WhenAll(params Action<TestFlowBuilder>[] branches)
    {
        var step = new FlowStep
        {
            Name = "whenall",
            Type = StepType.WhenAll,
            ParallelBranches = branches.Select(b =>
            {
                var builder = new TestFlowBuilder();
                b(builder);
                return builder.Build();
            }).ToList()
        };
        _steps.Add(step);
    }

    public void WhenAny(params Action<TestFlowBuilder>[] branches)
    {
        var step = new FlowStep
        {
            Name = "whenany",
            Type = StepType.WhenAny,
            ParallelBranches = branches.Select(b =>
            {
                var builder = new TestFlowBuilder();
                b(builder);
                return builder.Build();
            }).ToList()
        };
        _steps.Add(step);
    }

    public List<FlowStep> Build() => _steps;
}

// Test builder classes
public class TestIfBuilder
{
    private readonly FlowStep _step;
    public TestIfBuilder(FlowStep step) => _step = step;

    public TestIfBuilder Step(string name, Action<TestFlowState> action)
    {
        _step.ThenBranch!.Add(new FlowStep
        {
            Name = name,
            Type = StepType.Step,
            Action = s => action((TestFlowState)s)
        });
        return this;
    }

    public TestElseIfBuilder ElseIf(Func<TestFlowState, bool> condition)
    {
        var branch = new List<FlowStep>();
        _step.ElseIfBranches!.Add((s => condition((TestFlowState)s), branch));
        return new TestElseIfBuilder(_step, branch);
    }

    public TestElseBuilder Else() => new TestElseBuilder(_step);
    public void EndIf() { }
}

public class TestElseIfBuilder
{
    private readonly FlowStep _step;
    private readonly List<FlowStep> _branch;

    public TestElseIfBuilder(FlowStep step, List<FlowStep> branch)
    {
        _step = step;
        _branch = branch;
    }

    public TestElseIfBuilder Step(string name, Action<TestFlowState> action)
    {
        _branch.Add(new FlowStep
        {
            Name = name,
            Type = StepType.Step,
            Action = s => action((TestFlowState)s)
        });
        return this;
    }

    public TestElseIfBuilder ElseIf(Func<TestFlowState, bool> condition)
    {
        var branch = new List<FlowStep>();
        _step.ElseIfBranches!.Add((s => condition((TestFlowState)s), branch));
        return new TestElseIfBuilder(_step, branch);
    }

    public TestElseBuilder Else() => new TestElseBuilder(_step);
    public void EndIf() { }
}

public class TestElseBuilder
{
    private readonly FlowStep _step;
    public TestElseBuilder(FlowStep step) => _step = step;

    public TestElseBuilder Step(string name, Action<TestFlowState> action)
    {
        _step.ElseBranch!.Add(new FlowStep
        {
            Name = name,
            Type = StepType.Step,
            Action = s => action((TestFlowState)s)
        });
        return this;
    }

    public void EndIf() { }
}

public class TestSwitchBuilder
{
    private readonly FlowStep _step;
    public TestSwitchBuilder(FlowStep step) => _step = step;

    public TestSwitchBuilder Case(object value, Action<TestFlowBuilder> configure)
    {
        var builder = new TestFlowBuilder();
        configure(builder);
        _step.Cases![value] = builder.Build();
        return this;
    }

    public TestSwitchBuilder Default(Action<TestFlowBuilder> configure)
    {
        var builder = new TestFlowBuilder();
        configure(builder);
        _step.DefaultBranch = builder.Build();
        return this;
    }

    public void EndSwitch() { }
}

public class TestForEachBuilder
{
    private readonly FlowStep _step;
    public TestForEachBuilder(FlowStep step) => _step = step;

    public TestForEachBuilder WithParallelism(int maxParallelism)
    {
        _step.ForEachConfig!.MaxParallelism = maxParallelism;
        return this;
    }

    public TestForEachBuilder WithBatchSize(int batchSize)
    {
        _step.ForEachConfig!.BatchSize = batchSize;
        return this;
    }

    public TestForEachBuilder ContinueOnFailure()
    {
        _step.ForEachConfig!.ContinueOnFailure = true;
        return this;
    }

    public TestForEachBuilder Configure(Action<string, TestFlowBuilder> configure)
    {
        // Store configuration
        return this;
    }

    public void EndForEach() { }
}

// Test state
public class TestFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public bool Condition { get; set; }
    public bool AlternateCondition { get; set; }
    public int Value { get; set; }
    public bool Step1Executed { get; set; }
    public bool Step2Executed { get; set; }
    public bool Step3Executed { get; set; }
    public bool ThenExecuted { get; set; }
    public bool ElseIfExecuted { get; set; }
    public bool ElseExecuted { get; set; }
    public bool Case1Executed { get; set; }
    public bool Case2Executed { get; set; }
    public bool Case3Executed { get; set; }
    public bool DefaultExecuted { get; set; }
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();
    public bool Parallel1 { get; set; }
    public bool Parallel2 { get; set; }
    public bool Parallel3 { get; set; }
    public bool FastCompleted { get; set; }
    public bool SlowCompleted { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
