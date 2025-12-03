using Catga.Scheduling;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Distributed;

public class MessageSchedulerTests
{
    [Fact]
    public void ScheduledMessageHandle_StoresProperties()
    {
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var handle = new ScheduledMessageHandle
        {
            ScheduleId = "schedule-123",
            DeliverAt = deliverAt,
            MessageType = "TestMessage"
        };

        handle.ScheduleId.Should().Be("schedule-123");
        handle.DeliverAt.Should().Be(deliverAt);
        handle.MessageType.Should().Be("TestMessage");
    }

    [Fact]
    public void ScheduledMessageInfo_StoresAllProperties()
    {
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var createdAt = DateTimeOffset.UtcNow;

        var info = new ScheduledMessageInfo
        {
            ScheduleId = "schedule-123",
            MessageType = "TestMessage",
            DeliverAt = deliverAt,
            CreatedAt = createdAt,
            Status = ScheduledMessageStatus.Pending,
            RetryCount = 2,
            LastError = "Connection failed"
        };

        info.ScheduleId.Should().Be("schedule-123");
        info.MessageType.Should().Be("TestMessage");
        info.DeliverAt.Should().Be(deliverAt);
        info.CreatedAt.Should().Be(createdAt);
        info.Status.Should().Be(ScheduledMessageStatus.Pending);
        info.RetryCount.Should().Be(2);
        info.LastError.Should().Be("Connection failed");
    }

    [Fact]
    public void ScheduledMessageStatus_HasExpectedValues()
    {
        ((byte)ScheduledMessageStatus.Pending).Should().Be(0);
        ((byte)ScheduledMessageStatus.Processing).Should().Be(1);
        ((byte)ScheduledMessageStatus.Delivered).Should().Be(2);
        ((byte)ScheduledMessageStatus.Cancelled).Should().Be(3);
        ((byte)ScheduledMessageStatus.Failed).Should().Be(4);
    }

    [Fact]
    public void MessageSchedulerOptions_HasSensibleDefaults()
    {
        var options = new MessageSchedulerOptions();

        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.BatchSize.Should().Be(100);
        options.MaxRetries.Should().Be(3);
        options.RetryDelay.Should().Be(TimeSpan.FromSeconds(5));
        options.KeyPrefix.Should().Be("catga:schedule:");
    }

    [Fact]
    public void MessageSchedulerOptions_CanBeCustomized()
    {
        var options = new MessageSchedulerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(500),
            BatchSize = 50,
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromSeconds(10),
            KeyPrefix = "myapp:schedule:"
        };

        options.PollingInterval.Should().Be(TimeSpan.FromMilliseconds(500));
        options.BatchSize.Should().Be(50);
        options.MaxRetries.Should().Be(5);
        options.RetryDelay.Should().Be(TimeSpan.FromSeconds(10));
        options.KeyPrefix.Should().Be("myapp:schedule:");
    }
}
