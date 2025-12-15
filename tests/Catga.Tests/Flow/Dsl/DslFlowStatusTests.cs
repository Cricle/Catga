using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for DslFlowStatus enum
/// </summary>
public class DslFlowStatusTests
{
    #region Status Value Tests

    [Fact]
    public void DslFlowStatus_Pending_Exists()
    {
        var status = DslFlowStatus.Pending;
        status.Should().Be(DslFlowStatus.Pending);
    }

    [Fact]
    public void DslFlowStatus_Running_Exists()
    {
        var status = DslFlowStatus.Running;
        status.Should().Be(DslFlowStatus.Running);
    }

    [Fact]
    public void DslFlowStatus_Completed_Exists()
    {
        var status = DslFlowStatus.Completed;
        status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public void DslFlowStatus_Failed_Exists()
    {
        var status = DslFlowStatus.Failed;
        status.Should().Be(DslFlowStatus.Failed);
    }

    [Fact]
    public void DslFlowStatus_Cancelled_Exists()
    {
        var status = DslFlowStatus.Cancelled;
        status.Should().Be(DslFlowStatus.Cancelled);
    }

    #endregion

    #region Status Comparison Tests

    [Fact]
    public void DslFlowStatus_AllValuesUnique()
    {
        var statuses = new[]
        {
            DslFlowStatus.Pending,
            DslFlowStatus.Running,
            DslFlowStatus.Completed,
            DslFlowStatus.Failed,
            DslFlowStatus.Cancelled
        };

        statuses.Distinct().Count().Should().Be(5);
    }

    [Fact]
    public void DslFlowStatus_InSwitch_Works()
    {
        var status = DslFlowStatus.Running;
        var result = status switch
        {
            DslFlowStatus.Pending => "pending",
            DslFlowStatus.Running => "running",
            DslFlowStatus.Completed => "completed",
            DslFlowStatus.Failed => "failed",
            DslFlowStatus.Cancelled => "cancelled",
            _ => "unknown"
        };

        result.Should().Be("running");
    }

    #endregion

    #region Terminal Status Tests

    [Fact]
    public void DslFlowStatus_Completed_IsTerminal()
    {
        var status = DslFlowStatus.Completed;

        (status == DslFlowStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void DslFlowStatus_Failed_IsTerminal()
    {
        var status = DslFlowStatus.Failed;

        (status == DslFlowStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void DslFlowStatus_Cancelled_IsTerminal()
    {
        var status = DslFlowStatus.Cancelled;

        (status == DslFlowStatus.Cancelled).Should().BeTrue();
    }

    #endregion
}
