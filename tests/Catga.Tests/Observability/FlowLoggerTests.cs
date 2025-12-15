using Catga.Observability;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive tests for FlowLogger
/// </summary>
public class FlowLoggerTests
{
    private readonly ILogger _logger;

    public FlowLoggerTests()
    {
        _logger = Substitute.For<ILogger>();
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
    }

    #region Flow Lifecycle Tests

    [Fact]
    public void LogFlowStarted_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowStarted(_logger, "TestFlow", "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowStarted_WithNullFlowId_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowStarted(_logger, "TestFlow");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowCompleted_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowCompleted(_logger, "TestFlow", 123.45, "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowCompleted_WithNullFlowId_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowCompleted(_logger, "TestFlow", 123.45);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowFailed_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowFailed(_logger, "TestFlow", "Error message", 100.0, "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowFailed_WithNullFlowId_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowFailed(_logger, "TestFlow", "Error message", 100.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowResumed_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowResumed(_logger, "TestFlow", 5, "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogFlowResumed_WithNullFlowId_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowResumed(_logger, "TestFlow", 5);
        act.Should().NotThrow();
    }

    #endregion

    #region Step Lifecycle Tests

    [Fact]
    public void LogStepStarted_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepStarted(_logger, "TestFlow", 0, "Send", "payment");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepStarted_WithNullTag_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepStarted(_logger, "TestFlow", 0, "Send");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepCompleted_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepCompleted(_logger, "TestFlow", 0, "Send", 45.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepFailed_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepFailed(_logger, "TestFlow", 0, "Send", "Step error");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepSkipped_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepSkipped(_logger, "TestFlow", 0, "Send", "Condition not met");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepRetried_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepRetried(_logger, "TestFlow", 0, "Send", 2, "Timeout");
        act.Should().NotThrow();
    }

    #endregion

    #region Branch Tests

    [Fact]
    public void LogBranchEntered_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogBranchEntered(_logger, "TestFlow", "If", 0);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogBranchEntered_WithSwitchBranch_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogBranchEntered(_logger, "TestFlow", "Switch", 2);
        act.Should().NotThrow();
    }

    #endregion

    #region ForEach Tests

    [Fact]
    public void LogForEachStarted_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogForEachStarted(_logger, "TestFlow", 0, 10, 4);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogForEachCompleted_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogForEachCompleted(_logger, "TestFlow", 0, 10, 500.0);
        act.Should().NotThrow();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void LogFlowStarted_WithEmptyFlowName_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogFlowStarted(_logger, "", "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepCompleted_WithZeroDuration_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepCompleted(_logger, "TestFlow", 0, "Send", 0);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogStepCompleted_WithNegativeStepIndex_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogStepCompleted(_logger, "TestFlow", -1, "Send", 50.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogForEachStarted_WithZeroItems_ShouldNotThrow()
    {
        var act = () => FlowLogger.LogForEachStarted(_logger, "TestFlow", 0, 0, 1);
        act.Should().NotThrow();
    }

    #endregion
}
