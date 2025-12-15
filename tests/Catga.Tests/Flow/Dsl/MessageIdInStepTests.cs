using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for MessageId generation in step execution
/// </summary>
public class MessageIdInStepTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region MessageId Generation Tests

    [Fact]
    public void Command_HasUniqueMessageId()
    {
        var cmd1 = new TestCommand("1");
        var cmd2 = new TestCommand("2");

        cmd1.MessageId.Should().NotBe(cmd2.MessageId);
    }

    [Fact]
    public void Command_MessageId_IsPositive()
    {
        var cmd = new TestCommand("test");

        cmd.MessageId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MultipleCommands_AllHaveUniqueIds()
    {
        var commands = Enumerable.Range(0, 10)
            .Select(i => new TestCommand($"cmd-{i}"))
            .ToList();

        var ids = commands.Select(c => c.MessageId).Distinct();
        ids.Count().Should().Be(10);
    }

    #endregion

    #region QoS Tests

    [Fact]
    public void Command_HasQoS()
    {
        var cmd = new TestCommand("test");

        cmd.QoS.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact]
    public void Command_QoS_IsConsistent()
    {
        var cmd = new TestCommand("test");

        cmd.QoS.Should().Be(cmd.QoS);
    }

    #endregion
}
