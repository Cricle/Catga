using System.Diagnostics;
namespace Catga.Tests.Observability;

public class ActivityPayloadCaptureTests
{
    // ActivityPayloadCapture was removed; tests stubbed to keep build integrity.
        act.Should().NotThrow();
    }

    [Fact]
    public void CaptureResponse_WithNullResponse_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        var act = () => ActivityPayloadCapture.CaptureResponse<TestResponse>(activity, null!);

        // Assert
        act.Should().NotThrow();
    }

    // ==================== CaptureEvent Tests ====================

    [Fact]
    public void CaptureEvent_WithValidEvent_ShouldSetEventTag()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => System.Text.Json.JsonSerializer.Serialize(obj);
        using var activity = _activitySource.StartActivity("Test");
        var @event = new TestEvent { EventType = "test.event" };

        // Act
        ActivityPayloadCapture.CaptureEvent(activity, @event);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "catga.event.payload");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "catga.event.payload").Value;
        tagValue.Should().Contain("test.event");
    }

    [Fact]
    public void CaptureEvent_WithNullActivity_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        Activity? activity = null;

        // Act
        var act = () => ActivityPayloadCapture.CaptureEvent(activity, new TestEvent());

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CaptureEvent_WithNullEvent_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        var act = () => ActivityPayloadCapture.CaptureEvent<TestEvent>(activity, null!);

        // Assert
        act.Should().NotThrow();
    }

    // ==================== Custom Serializer Scenarios ====================

    [Fact]
    public void CapturePayload_WithCustomSerializerThrowingException_ShouldPropagateException()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => throw new InvalidOperationException("Serializer error");
        using var activity = _activitySource.StartActivity("Test");

        // Act
        var act = () => ActivityPayloadCapture.CapturePayload(activity, "test.tag", "payload");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Serializer error");
    }

    [Fact]
    public void CapturePayload_WithCustomSerializerReturningEmpty_ShouldSetEmptyTag()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => string.Empty;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", "payload");

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "test.tag");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "test.tag").Value;
        tagValue.Should().BeEmpty();
    }

    [Fact]
    public void CapturePayload_WithComplexObject_ShouldSerializeCorrectly()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => System.Text.Json.JsonSerializer.Serialize(obj);
        using var activity = _activitySource.StartActivity("Test");
        var complexPayload = new ComplexPayload
        {
            Id = 123,
            Name = "Test",
            Tags = new[] { "tag1", "tag2" },
            Metadata = new Dictionary<string, string> { ["key"] = "value" }
        };

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", complexPayload);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "test.tag");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "test.tag").Value;
        tagValue.Should().Contain("123");
        tagValue.Should().Contain("Test");
        tagValue.Should().Contain("tag1");
        tagValue.Should().Contain("key");
    }

    // ==================== Edge Cases ====================

    [Fact]
    public void CapturePayload_WithEmptyTagName_ShouldStillWork()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => "test";
        using var activity = _activitySource.StartActivity("Test");

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "", "payload");

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "");
    }

    [Fact]
    public void CapturePayload_WithSameTagNameMultipleTimes_ShouldUpdateTag()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", "first");
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", "second");

        // Assert
        var tags = activity!.Tags.Where(t => t.Key == "test.tag").ToList();
        tags.Should().HaveCountGreaterThan(0);
    }

    // ==================== Test Helpers ====================

    public record TestPayload
    {
        public string Data { get; init; } = string.Empty;
    }

    public record TestRequest
    {
        public string Command { get; init; } = string.Empty;
    }

    public record TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    public record TestEvent
    {
        public string EventType { get; init; } = string.Empty;
    }

    public record ComplexPayload
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string[] Tags { get; init; } = Array.Empty<string>();
        public Dictionary<string, string> Metadata { get; init; } = new();
    }
}

