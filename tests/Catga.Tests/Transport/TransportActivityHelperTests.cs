using System.Diagnostics;
using Catga.Transport;
using FluentAssertions;

namespace Catga.Tests.Transport;

public class TransportActivityHelperTests
{
    [Fact]
    public void SetPublishTags_WithNullActivity_DoesNotThrow()
    {
        var action = () => TransportActivityHelper.SetPublishTags(
            null, "redis", "orders", "OrderCreated");

        action.Should().NotThrow();
    }

    [Fact]
    public void SetPublishTags_SetsAllRequiredTags()
    {
        using var activity = new Activity("Test").Start();

        TransportActivityHelper.SetPublishTags(
            activity, "redis", "orders", "OrderCreated", "msg-123", "AtLeastOnce");

        activity.Tags.Should().Contain(t => t.Key == "messaging.system" && t.Value == "redis");
        activity.Tags.Should().Contain(t => t.Key == "messaging.destination.name" && t.Value == "orders");
        activity.Tags.Should().Contain(t => t.Key == "messaging.operation" && t.Value == "publish");
        activity.Tags.Should().Contain(t => t.Key == "catga.message.type" && t.Value == "OrderCreated");
        activity.Tags.Should().Contain(t => t.Key == "catga.message.id" && t.Value == "msg-123");
        activity.Tags.Should().Contain(t => t.Key == "catga.qos" && t.Value == "AtLeastOnce");
    }

    [Fact]
    public void SetPublishTags_WithoutOptionalTags_OnlySetsRequired()
    {
        using var activity = new Activity("Test").Start();

        TransportActivityHelper.SetPublishTags(
            activity, "nats", "events", "UserCreated");

        activity.Tags.Should().Contain(t => t.Key == "messaging.system" && t.Value == "nats");
        activity.Tags.Should().Contain(t => t.Key == "messaging.destination.name" && t.Value == "events");
        activity.Tags.Should().NotContain(t => t.Key == "catga.message.id");
        activity.Tags.Should().NotContain(t => t.Key == "catga.qos");
    }

    [Fact]
    public void SetSubscribeTags_WithNullActivity_DoesNotThrow()
    {
        var action = () => TransportActivityHelper.SetSubscribeTags(
            null, "redis", "orders", "OrderCreated");

        action.Should().NotThrow();
    }

    [Fact]
    public void SetSubscribeTags_SetsAllRequiredTags()
    {
        using var activity = new Activity("Test").Start();

        TransportActivityHelper.SetSubscribeTags(
            activity, "redis", "orders", "OrderCreated", "msg-456");

        activity.Tags.Should().Contain(t => t.Key == "messaging.system" && t.Value == "redis");
        activity.Tags.Should().Contain(t => t.Key == "messaging.destination.name" && t.Value == "orders");
        activity.Tags.Should().Contain(t => t.Key == "messaging.operation" && t.Value == "receive");
        activity.Tags.Should().Contain(t => t.Key == "catga.message.type" && t.Value == "OrderCreated");
        activity.Tags.Should().Contain(t => t.Key == "catga.message.id" && t.Value == "msg-456");
    }

    [Fact]
    public void SetSubscribeTags_WithoutMessageId_DoesNotSetMessageIdTag()
    {
        using var activity = new Activity("Test").Start();

        TransportActivityHelper.SetSubscribeTags(
            activity, "nats", "events", "UserCreated");

        activity.Tags.Should().NotContain(t => t.Key == "catga.message.id");
    }

    [Theory]
    [InlineData("redis", "orders", "OrderCreated")]
    [InlineData("nats", "users", "UserRegistered")]
    [InlineData("inmemory", "notifications", "NotificationSent")]
    public void SetPublishTags_HandlesVariousSystems(string system, string destination, string messageType)
    {
        using var activity = new Activity("Test").Start();

        TransportActivityHelper.SetPublishTags(activity, system, destination, messageType);

        activity.Tags.Should().Contain(t => t.Key == "messaging.system" && t.Value == system);
        activity.Tags.Should().Contain(t => t.Key == "messaging.destination.name" && t.Value == destination);
        activity.Tags.Should().Contain(t => t.Key == "catga.message.type" && t.Value == messageType);
    }
}
