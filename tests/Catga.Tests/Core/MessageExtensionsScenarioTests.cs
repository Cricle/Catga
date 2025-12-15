using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive scenario tests for MessageExtensions
/// </summary>
public class MessageExtensionsScenarioTests
{
    private record TestMessage(string Data) : IMessage
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Message ID Generation Tests

    [Fact]
    public void NewMessageId_SequentialCalls_AreIncreasing()
    {
        var ids = new List<long>();
        for (int i = 0; i < 100; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }

        for (int i = 1; i < ids.Count; i++)
        {
            ids[i].Should().BeGreaterThan(ids[i - 1]);
        }
    }

    [Fact]
    public void NewMessageId_HighVolume_NoCollisions()
    {
        var ids = new HashSet<long>();
        for (int i = 0; i < 10000; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }

        ids.Count.Should().Be(10000);
    }

    [Fact]
    public void NewMessageId_ParallelGeneration_NoCollisions()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(MessageExtensions.NewMessageId());
        });

        ids.Distinct().Count().Should().Be(1000);
    }

    #endregion

    #region Message Type Tests

    [Fact]
    public void Message_WithCustomId_UsesProvidedId()
    {
        var customId = 12345L;
        var message = new TestMessage("test") { MessageId = customId };

        message.MessageId.Should().Be(customId);
    }

    [Fact]
    public void Message_DefaultId_IsGenerated()
    {
        var message = new TestMessage("test");

        message.MessageId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Message_MultipleInstances_HaveUniqueIds()
    {
        var messages = Enumerable.Range(0, 100)
            .Select(i => new TestMessage($"msg-{i}"))
            .ToList();

        messages.Select(m => m.MessageId).Distinct().Count().Should().Be(100);
    }

    #endregion

    #region QoS Tests

    [Theory]
    [InlineData(QualityOfService.AtMostOnce)]
    [InlineData(QualityOfService.AtLeastOnce)]
    [InlineData(QualityOfService.ExactlyOnce)]
    public void QoS_AllValues_AreValid(QualityOfService qos)
    {
        Enum.IsDefined(typeof(QualityOfService), qos).Should().BeTrue();
    }

    [Fact]
    public void QoS_DefaultValue_IsAtLeastOnce()
    {
        var message = new TestMessage("test");
        message.QoS.Should().Be(QualityOfService.AtLeastOnce);
    }

    #endregion
}
