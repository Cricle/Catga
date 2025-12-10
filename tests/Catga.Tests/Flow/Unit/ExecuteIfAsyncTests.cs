using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;
using System.Diagnostics;

namespace Catga.Tests.Flow.Unit;

/// <summary>
/// 针对 ExecuteIfAsync 方法的专门单元测试
/// </summary>
public class ExecuteIfAsyncTests
{
    [Fact]
    public async Task ExecuteIfAsync_WithTrueCondition_ShouldExecuteThenBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleIfFlow();

        var state = new IfTestState
        {
            FlowId = "if-test-true",
            ShouldExecuteThen = true,
            ExecutedBranch = string.Empty
        };

        mediator.SendAsync(Arg.Any<SetBranchCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<SetBranchCommand>();
                state.ExecutedBranch = cmd.BranchName;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<IfTestState, SimpleIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("if flow should succeed");
        state.ExecutedBranch.Should().Be("Then", "true condition should execute Then branch");
    }

    [Fact]
    public async Task ExecuteIfAsync_WithFalseCondition_ShouldExecuteElseBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleIfFlow();

        var state = new IfTestState
        {
            FlowId = "if-test-false",
            ShouldExecuteThen = false,
            ExecutedBranch = string.Empty
        };

        mediator.SendAsync(Arg.Any<SetBranchCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<SetBranchCommand>();
                state.ExecutedBranch = cmd.BranchName;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<IfTestState, SimpleIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("if flow should succeed");
        state.ExecutedBranch.Should().Be("Else", "false condition should execute Else branch");
    }

    [Fact]
    public async Task ExecuteIfAsync_WithElseIfConditions_ShouldExecuteMatchingElseIf()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ComplexIfFlow();

        var state = new ComplexIfTestState
        {
            FlowId = "complex-if-test",
            Value = 2, // 应该匹配第二个 ElseIf 条件
            ExecutedBranch = string.Empty
        };

        mediator.SendAsync(Arg.Any<SetBranchCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<SetBranchCommand>();
                state.ExecutedBranch = cmd.BranchName;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<ComplexIfTestState, ComplexIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("complex if flow should succeed");
        state.ExecutedBranch.Should().Be("ElseIf2", "value 2 should match second ElseIf condition");
    }

    [Fact]
    public async Task ExecuteIfAsync_WithNoMatchingConditions_ShouldExecuteElseBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ComplexIfFlow();

        var state = new ComplexIfTestState
        {
            FlowId = "complex-if-no-match",
            Value = 99, // 不匹配任何条件
            ExecutedBranch = string.Empty
        };

        mediator.SendAsync(Arg.Any<SetBranchCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<SetBranchCommand>();
                state.ExecutedBranch = cmd.BranchName;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<ComplexIfTestState, ComplexIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("complex if flow should succeed");
        state.ExecutedBranch.Should().Be("Else", "no matching condition should execute Else branch");
    }

    [Fact]
    public async Task ExecuteIfAsync_WithNestedIfConditions_ShouldExecuteCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NestedIfFlow();

        var state = new NestedIfTestState
        {
            FlowId = "nested-if-test",
            OuterCondition = true,
            InnerCondition = false,
            ExecutedBranches = []
        };

        mediator.SendAsync(Arg.Any<SetBranchCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<SetBranchCommand>();
                state.ExecutedBranches.Add(cmd.BranchName);
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<NestedIfTestState, NestedIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("nested if flow should succeed");
        state.ExecutedBranches.Should().Contain("OuterThen", "outer condition is true");
        state.ExecutedBranches.Should().Contain("InnerElse", "inner condition is false");
        state.ExecutedBranches.Should().HaveCount(2, "should execute exactly two branches");
    }

    [Fact]
    public async Task ExecuteIfAsync_Performance_ShouldHandleComplexBranchingEfficiently()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new PerformanceIfFlow();

        var processedCount = 0;
        var state = new PerformanceIfTestState
        {
            FlowId = "performance-if-test",
            Items = Enumerable.Range(1, 1000).Select(i => new PerformanceItem
            {
                Id = i,
                Category = (i % 3) switch { 0 => "A", 1 => "B", _ => "C" }
            }).ToList()
        };

        mediator.SendAsync(Arg.Any<ProcessPerformanceItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Interlocked.Increment(ref processedCount);
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<PerformanceIfTestState, PerformanceIfFlow>(mediator, store, config);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("performance if flow should succeed");
        processedCount.Should().Be(1000, "all items should be processed");

        var throughput = 1000 / stopwatch.Elapsed.TotalSeconds;
        throughput.Should().BeGreaterThan(5000, "should maintain high throughput for complex branching");

        Console.WriteLine($"Complex Branching Performance: 1000 items in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Throughput: {throughput:F0} items/sec");
    }

    [Fact]
    public async Task ExecuteIfAsync_WithEmptyBranches_ShouldHandleGracefully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyBranchIfFlow();

        var state = new IfTestState
        {
            FlowId = "empty-branch-test",
            ShouldExecuteThen = true,
            ExecutedBranch = string.Empty
        };

        var executor = new DslFlowExecutor<IfTestState, EmptyBranchIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("empty branch flow should succeed");
        // 空分支不应该执行任何命令，所以 ExecutedBranch 应该保持空
        state.ExecutedBranch.Should().BeEmpty("empty branch should not execute any commands");
    }
}

// 测试用的流配置
public class SimpleIfFlow : FlowConfig<IfTestState>
{
    protected override void Configure(IFlowBuilder<IfTestState> flow)
    {
        flow.Name("simple-if-flow");

        flow.If(s => s.ShouldExecuteThen)
            .Send(s => new SetBranchCommand { BranchName = "Then" })
            .Else()
            .Send(s => new SetBranchCommand { BranchName = "Else" })
            .EndIf();
    }
}

public class ComplexIfFlow : FlowConfig<ComplexIfTestState>
{
    protected override void Configure(IFlowBuilder<ComplexIfTestState> flow)
    {
        flow.Name("complex-if-flow");

        flow.If(s => s.Value == 0)
            .Send(s => new SetBranchCommand { BranchName = "Then" })
            .ElseIf(s => s.Value == 1)
            .Send(s => new SetBranchCommand { BranchName = "ElseIf1" })
            .ElseIf(s => s.Value == 2)
            .Send(s => new SetBranchCommand { BranchName = "ElseIf2" })
            .ElseIf(s => s.Value == 3)
            .Send(s => new SetBranchCommand { BranchName = "ElseIf3" })
            .Else()
            .Send(s => new SetBranchCommand { BranchName = "Else" })
            .EndIf();
    }
}

public class NestedIfFlow : FlowConfig<NestedIfTestState>
{
    protected override void Configure(IFlowBuilder<NestedIfTestState> flow)
    {
        flow.Name("nested-if-flow");

        flow.If(s => s.OuterCondition)
            .Send(s => new SetBranchCommand { BranchName = "OuterThen" })
            .If(s => s.InnerCondition)
                .Send(s => new SetBranchCommand { BranchName = "InnerThen" })
                .Else()
                .Send(s => new SetBranchCommand { BranchName = "InnerElse" })
                .EndIf()
            .Else()
            .Send(s => new SetBranchCommand { BranchName = "OuterElse" })
            .EndIf();
    }
}

public class PerformanceIfFlow : FlowConfig<PerformanceIfTestState>
{
    protected override void Configure(IFlowBuilder<PerformanceIfTestState> flow)
    {
        flow.Name("performance-if-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) => f
                .If(s => item.Category == "A")
                    .Send(s => new ProcessPerformanceItemCommand { Item = item, ProcessingType = "TypeA" })
                    .ElseIf(s => item.Category == "B")
                    .Send(s => new ProcessPerformanceItemCommand { Item = item, ProcessingType = "TypeB" })
                    .Else()
                    .Send(s => new ProcessPerformanceItemCommand { Item = item, ProcessingType = "TypeC" })
                    .EndIf())
            .EndForEach();
    }
}

public class EmptyBranchIfFlow : FlowConfig<IfTestState>
{
    protected override void Configure(IFlowBuilder<IfTestState> flow)
    {
        flow.Name("empty-branch-if-flow");

        flow.If(s => s.ShouldExecuteThen)
            // 空的 Then 分支
        .Else()
            // 空的 Else 分支
        .EndIf();
    }
}

// 测试状态定义
public class IfTestState : IFlowState
{
    public string? FlowId { get; set; }
    public bool ShouldExecuteThen { get; set; }
    public string ExecutedBranch { get; set; } = string.Empty;

    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ComplexIfTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public string ExecutedBranch { get; set; } = string.Empty;

    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class NestedIfTestState : IFlowState
{
    public string? FlowId { get; set; }
    public bool OuterCondition { get; set; }
    public bool InnerCondition { get; set; }
    public List<string> ExecutedBranches { get; set; } = [];

    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class PerformanceIfTestState : IFlowState
{
    public string? FlowId { get; set; }
    public List<PerformanceItem> Items { get; set; } = [];

    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class PerformanceItem
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
}

// 测试命令定义
public record SetBranchCommand : IRequest
{
    public required string BranchName { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ProcessPerformanceItemCommand : IRequest
{
    public required PerformanceItem Item { get; init; }
    public required string ProcessingType { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
