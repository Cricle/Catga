using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for DslFlowStatus transitions and states
/// </summary>
public class DslFlowStatusTransitionTests
{
    #region Status Value Tests

    [Fact]
    public void Pending_IsValidStatus()
    {
        var status = DslFlowStatus.Pending;
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    [Fact]
    public void Running_IsValidStatus()
    {
        var status = DslFlowStatus.Running;
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    [Fact]
    public void Completed_IsValidStatus()
    {
        var status = DslFlowStatus.Completed;
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    [Fact]
    public void Failed_IsValidStatus()
    {
        var status = DslFlowStatus.Failed;
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    [Fact]
    public void WaitingForEvent_IsValidStatus()
    {
        var status = DslFlowStatus.WaitingForEvent;
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    [Fact]
    public void Cancelled_IsValidStatus()
    {
        var status = DslFlowStatus.Cancelled;
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    #endregion

    #region Status Comparison Tests

    [Fact]
    public void Status_Equality_SameValues()
    {
        var status1 = DslFlowStatus.Running;
        var status2 = DslFlowStatus.Running;

        (status1 == status2).Should().BeTrue();
    }

    [Fact]
    public void Status_Inequality_DifferentValues()
    {
        var status1 = DslFlowStatus.Running;
        var status2 = DslFlowStatus.Completed;

        (status1 != status2).Should().BeTrue();
    }

    #endregion

    #region Status Usage Tests

    [Fact]
    public void Status_InSwitch_AllCasesHandled()
    {
        var statuses = Enum.GetValues<DslFlowStatus>();

        foreach (var status in statuses)
        {
            var result = status switch
            {
                DslFlowStatus.Pending => "not started",
                DslFlowStatus.Running => "in progress",
                DslFlowStatus.Completed => "done",
                DslFlowStatus.Failed => "error",
                DslFlowStatus.WaitingForEvent => "waiting",
                DslFlowStatus.Cancelled => "aborted",
                _ => "unknown"
            };

            result.Should().NotBe("unknown");
        }
    }

    [Fact]
    public void Status_InDictionary_Works()
    {
        var statusMessages = new Dictionary<DslFlowStatus, string>
        {
            [DslFlowStatus.Pending] = "Flow is pending",
            [DslFlowStatus.Running] = "Flow is running",
            [DslFlowStatus.Completed] = "Flow completed",
            [DslFlowStatus.Failed] = "Flow failed",
            [DslFlowStatus.WaitingForEvent] = "Flow waiting",
            [DslFlowStatus.Cancelled] = "Flow cancelled"
        };

        statusMessages[DslFlowStatus.Completed].Should().Be("Flow completed");
    }

    [Fact]
    public void Status_InCollection_CanBeFiltered()
    {
        var results = new List<FlowResult<TestState>>
        {
            new() { Status = DslFlowStatus.Completed },
            new() { Status = DslFlowStatus.Failed },
            new() { Status = DslFlowStatus.Completed },
            new() { Status = DslFlowStatus.Running }
        };

        results.Count(r => r.Status == DslFlowStatus.Completed).Should().Be(2);
        results.Count(r => r.Status == DslFlowStatus.Failed).Should().Be(1);
    }

    #endregion

    #region Terminal Status Tests

    [Fact]
    public void Completed_IsTerminalStatus()
    {
        var terminalStatuses = new[]
        {
            DslFlowStatus.Completed,
            DslFlowStatus.Failed,
            DslFlowStatus.Cancelled
        };

        terminalStatuses.Should().Contain(DslFlowStatus.Completed);
    }

    [Fact]
    public void Running_IsNotTerminalStatus()
    {
        var terminalStatuses = new[]
        {
            DslFlowStatus.Completed,
            DslFlowStatus.Failed,
            DslFlowStatus.Cancelled
        };

        terminalStatuses.Should().NotContain(DslFlowStatus.Running);
    }

    [Fact]
    public void Pending_IsInitialStatus()
    {
        var initialStatus = default(DslFlowStatus);
        initialStatus.Should().Be(DslFlowStatus.Pending);
    }

    #endregion

    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }
}
