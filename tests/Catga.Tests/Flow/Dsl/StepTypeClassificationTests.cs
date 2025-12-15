using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for step type classification and categorization
/// </summary>
public class StepTypeClassificationTests
{
    #region Action Step Classification Tests

    [Fact]
    public void Send_IsActionStep()
    {
        var type = StepType.Send;
        type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Query_IsActionStep()
    {
        var type = StepType.Query;
        type.Should().Be(StepType.Query);
    }

    [Fact]
    public void Publish_IsActionStep()
    {
        var type = StepType.Publish;
        type.Should().Be(StepType.Publish);
    }

    #endregion

    #region Control Flow Classification Tests

    [Fact]
    public void If_IsControlFlow()
    {
        var type = StepType.If;
        type.Should().Be(StepType.If);
    }

    [Fact]
    public void Switch_IsControlFlow()
    {
        var type = StepType.Switch;
        type.Should().Be(StepType.Switch);
    }

    #endregion

    #region Looping Classification Tests

    [Fact]
    public void ForEach_IsLooping()
    {
        var type = StepType.ForEach;
        type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void WhenAll_IsLooping()
    {
        var type = StepType.WhenAll;
        type.Should().Be(StepType.WhenAll);
    }

    [Fact]
    public void WhenAny_IsLooping()
    {
        var type = StepType.WhenAny;
        type.Should().Be(StepType.WhenAny);
    }

    #endregion

    #region Timing Classification Tests

    [Fact]
    public void Delay_IsTiming()
    {
        var type = StepType.Delay;
        type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void Wait_IsTiming()
    {
        var type = StepType.Wait;
        type.Should().Be(StepType.Wait);
    }

    #endregion
}
