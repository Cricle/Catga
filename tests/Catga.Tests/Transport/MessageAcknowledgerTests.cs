using Catga.Transport;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Transport;

public class MessageAcknowledgerTests
{
    [Fact]
    public void AckContext_StoresProperties()
    {
        var acknowledger = new MockAcknowledger();
        var context = new AckContext
        {
            MessageId = "msg-123",
            Acknowledger = acknowledger,
            DeliveryAttempt = 2,
            MaxDeliveryAttempts = 5
        };

        context.MessageId.Should().Be("msg-123");
        context.Acknowledger.Should().Be(acknowledger);
        context.DeliveryAttempt.Should().Be(2);
        context.MaxDeliveryAttempts.Should().Be(5);
    }

    [Fact]
    public void AckContext_IsLastAttempt_WhenAtMax()
    {
        var context = new AckContext
        {
            MessageId = "msg-123",
            Acknowledger = new MockAcknowledger(),
            DeliveryAttempt = 5,
            MaxDeliveryAttempts = 5
        };

        context.IsLastAttempt.Should().BeTrue();
    }

    [Fact]
    public void AckContext_IsNotLastAttempt_WhenBelowMax()
    {
        var context = new AckContext
        {
            MessageId = "msg-123",
            Acknowledger = new MockAcknowledger(),
            DeliveryAttempt = 3,
            MaxDeliveryAttempts = 5
        };

        context.IsLastAttempt.Should().BeFalse();
    }

    [Fact]
    public async Task AckContext_AckAsync_DelegatesToAcknowledger()
    {
        var acknowledger = new MockAcknowledger();
        var context = new AckContext
        {
            MessageId = "msg-123",
            Acknowledger = acknowledger,
            DeliveryAttempt = 1,
            MaxDeliveryAttempts = 5
        };

        await context.AckAsync();

        acknowledger.AckedMessages.Should().Contain("msg-123");
    }

    [Fact]
    public async Task AckContext_NackAsync_DelegatesToAcknowledger()
    {
        var acknowledger = new MockAcknowledger();
        var context = new AckContext
        {
            MessageId = "msg-123",
            Acknowledger = acknowledger,
            DeliveryAttempt = 1,
            MaxDeliveryAttempts = 5
        };

        await context.NackAsync(requeue: true);

        acknowledger.NackedMessages.Should().Contain(("msg-123", true));
    }

    [Fact]
    public async Task AckContext_RejectAsync_DelegatesToAcknowledger()
    {
        var acknowledger = new MockAcknowledger();
        var context = new AckContext
        {
            MessageId = "msg-123",
            Acknowledger = acknowledger,
            DeliveryAttempt = 1,
            MaxDeliveryAttempts = 5
        };

        await context.RejectAsync("Invalid message");

        acknowledger.RejectedMessages.Should().Contain(("msg-123", "Invalid message"));
    }

    [Fact]
    public void AckMode_HasExpectedValues()
    {
        ((byte)AckMode.Auto).Should().Be(0);
        ((byte)AckMode.Manual).Should().Be(1);
        ((byte)AckMode.None).Should().Be(2);
    }

    [Fact]
    public void AckOptions_HasSensibleDefaults()
    {
        var options = new AckOptions();

        options.Mode.Should().Be(AckMode.Auto);
        options.MaxDeliveryAttempts.Should().Be(5);
        options.RedeliveryDelay.Should().Be(TimeSpan.FromSeconds(5));
        options.ExponentialBackoff.Should().BeTrue();
        options.MaxRedeliveryDelay.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AckOptions_CanBeCustomized()
    {
        var options = new AckOptions
        {
            Mode = AckMode.Manual,
            MaxDeliveryAttempts = 10,
            RedeliveryDelay = TimeSpan.FromSeconds(10),
            ExponentialBackoff = false,
            MaxRedeliveryDelay = TimeSpan.FromMinutes(10)
        };

        options.Mode.Should().Be(AckMode.Manual);
        options.MaxDeliveryAttempts.Should().Be(10);
        options.RedeliveryDelay.Should().Be(TimeSpan.FromSeconds(10));
        options.ExponentialBackoff.Should().BeFalse();
        options.MaxRedeliveryDelay.Should().Be(TimeSpan.FromMinutes(10));
    }

    private class MockAcknowledger : IMessageAcknowledger
    {
        public List<string> AckedMessages { get; } = new();
        public List<(string, bool)> NackedMessages { get; } = new();
        public List<(string, string?)> RejectedMessages { get; } = new();

        public ValueTask AckAsync(string messageId, CancellationToken ct = default)
        {
            AckedMessages.Add(messageId);
            return ValueTask.CompletedTask;
        }

        public ValueTask NackAsync(string messageId, bool requeue = true, CancellationToken ct = default)
        {
            NackedMessages.Add((messageId, requeue));
            return ValueTask.CompletedTask;
        }

        public ValueTask RejectAsync(string messageId, string? reason = null, CancellationToken ct = default)
        {
            RejectedMessages.Add((messageId, reason));
            return ValueTask.CompletedTask;
        }
    }
}
