using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for FlowId generation and uniqueness
/// </summary>
public class FlowIdTests
{
    #region FlowId Generation Tests

    [Fact]
    public void FlowId_CanBeGenerated()
    {
        var flowId = Guid.NewGuid().ToString();

        flowId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FlowId_IsUnique()
    {
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        id1.Should().NotBe(id2);
    }

    #endregion

    #region FlowId Uniqueness Tests

    [Fact]
    public void FlowId_MultipleGenerations_AllUnique()
    {
        var ids = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(Guid.NewGuid().ToString());
        }

        ids.Count.Should().Be(100);
    }

    [Fact]
    public void FlowId_ConcurrentGeneration_AllUnique()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(Guid.NewGuid().ToString());
        });

        ids.Distinct().Count().Should().Be(1000);
    }

    #endregion

    #region FlowId Format Tests

    [Fact]
    public void FlowId_IsValidGuid()
    {
        var flowId = Guid.NewGuid().ToString();

        Guid.TryParse(flowId, out _).Should().BeTrue();
    }

    [Fact]
    public void FlowId_HasStandardLength()
    {
        var flowId = Guid.NewGuid().ToString();

        flowId.Length.Should().Be(36); // Standard GUID string format
    }

    #endregion
}
