using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder default values and initialization
/// </summary>
public class FlowBuilderDefaultsTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region Default Values Tests

    [Fact]
    public void FlowBuilder_DefaultFlowName_IsNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.FlowName.Should().BeNull();
    }

    [Fact]
    public void FlowBuilder_DefaultSteps_IsEmpty()
    {
        var builder = new FlowBuilder<TestState>();

        builder.Steps.Should().BeEmpty();
    }

    [Fact]
    public void FlowBuilder_DefaultTimeout_Is10Minutes()
    {
        var builder = new FlowBuilder<TestState>();

        builder.DefaultTimeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void FlowBuilder_DefaultRetries_IsZero()
    {
        var builder = new FlowBuilder<TestState>();

        builder.DefaultRetries.Should().Be(0);
    }

    #endregion

    #region Collections Initialization Tests

    [Fact]
    public void FlowBuilder_TaggedTimeouts_IsEmpty()
    {
        var builder = new FlowBuilder<TestState>();

        builder.TaggedTimeouts.Should().BeEmpty();
    }

    [Fact]
    public void FlowBuilder_TaggedRetries_IsEmpty()
    {
        var builder = new FlowBuilder<TestState>();

        builder.TaggedRetries.Should().BeEmpty();
    }

    [Fact]
    public void FlowBuilder_TaggedPersist_IsEmpty()
    {
        var builder = new FlowBuilder<TestState>();

        builder.TaggedPersist.Should().BeEmpty();
    }

    #endregion

    #region Event Factories Initialization Tests

    [Fact]
    public void FlowBuilder_OnFlowCompletedFactory_IsNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.OnFlowCompletedFactory.Should().BeNull();
    }

    [Fact]
    public void FlowBuilder_OnFlowFailedFactory_IsNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.OnFlowFailedFactory.Should().BeNull();
    }

    [Fact]
    public void FlowBuilder_OnStepCompletedFactory_IsNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.OnStepCompletedFactory.Should().BeNull();
    }

    [Fact]
    public void FlowBuilder_OnStepFailedFactory_IsNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.OnStepFailedFactory.Should().BeNull();
    }

    #endregion
}
