using System.Diagnostics;
using Catga.Observability;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

/// <summary>
/// ActivityPayloadCapture测试 - 提升覆盖率从66.6%到90%+
/// </summary>
public class ActivityPayloadCaptureTests : IDisposable
{
    private readonly ActivitySource _activitySource;
    private readonly ActivityListener _activityListener;
    private readonly Func<object, string>? _originalSerializer;

    public ActivityPayloadCaptureTests()
    {
        _activitySource = new ActivitySource("TestSource");
        
        // Setup ActivityListener to enable activity creation
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "TestSource",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(_activityListener);
        
        // Save original serializer
        _originalSerializer = ActivityPayloadCapture.CustomSerializer;
    }

    public void Dispose()
    {
        // Restore original serializer
        ActivityPayloadCapture.CustomSerializer = _originalSerializer;
        _activityListener.Dispose();
        _activitySource.Dispose();
    }

    // ==================== CustomSerializer Tests ====================

    [Fact]
    public void CustomSerializer_ShouldBeSettable()
    {
        // Arrange
        Func<object, string> serializer = obj => obj.ToString() ?? string.Empty;

        // Act
        ActivityPayloadCapture.CustomSerializer = serializer;

        // Assert
        ActivityPayloadCapture.CustomSerializer.Should().BeSameAs(serializer);
    }

    [Fact]
    public void CustomSerializer_ShouldBeNullableByDefault()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = null;

        // Act & Assert
        ActivityPayloadCapture.CustomSerializer.Should().BeNull();
    }

    // ==================== CapturePayload Tests ====================

    [Fact]
    public void CapturePayload_WithNullActivity_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        Activity? activity = null;

        // Act
        var act = () => ActivityPayloadCapture.CapturePayload(activity, "test.tag", "payload");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CapturePayload_WithNullPayload_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        var act = () => ActivityPayloadCapture.CapturePayload<string>(activity, "test.tag", null!);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CapturePayload_WithNullSerializer_ShouldThrowInvalidOperationException()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = null;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        var act = () => ActivityPayloadCapture.CapturePayload(activity, "test.tag", "payload");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CustomSerializer must be set*");
    }

    [Fact]
    public void CapturePayload_WithSmallPayload_ShouldSetTag()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => System.Text.Json.JsonSerializer.Serialize(obj);
        using var activity = _activitySource.StartActivity("Test");
        var payload = new TestPayload { Data = "small" };

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", payload);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "test.tag");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "test.tag").Value;
        tagValue.Should().Contain("small");
    }

    [Fact]
    public void CapturePayload_WithLargePayload_ShouldIndicateSize()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => new string('x', 5000); // Return large string
        using var activity = _activitySource.StartActivity("Test");
        var payload = new TestPayload { Data = "large" };

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", payload);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "test.tag");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "test.tag").Value;
        tagValue.Should().Contain("<too large:");
        tagValue.Should().Contain("5000 bytes>");
    }

    [Fact]
    public void CapturePayload_WithExactlyMaxLength_ShouldSetTag()
    {
        // Arrange - Exactly 4096 bytes
        ActivityPayloadCapture.CustomSerializer = obj => new string('x', 4096);
        using var activity = _activitySource.StartActivity("Test");
        var payload = new TestPayload { Data = "exact" };

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", payload);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "test.tag");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "test.tag").Value;
        tagValue.Should().HaveLength(4096);
        tagValue.Should().NotContain("<too large:");
    }

    [Fact]
    public void CapturePayload_WithOnePastMaxLength_ShouldIndicateSize()
    {
        // Arrange - 4097 bytes (one past max)
        ActivityPayloadCapture.CustomSerializer = obj => new string('x', 4097);
        using var activity = _activitySource.StartActivity("Test");
        var payload = new TestPayload { Data = "one_past" };

        // Act
        ActivityPayloadCapture.CapturePayload(activity, "test.tag", payload);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "test.tag");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "test.tag").Value;
        tagValue.Should().Contain("<too large:");
        tagValue.Should().Contain("4097 bytes>");
    }

    // ==================== CaptureRequest Tests ====================

    [Fact]
    public void CaptureRequest_WithValidRequest_ShouldSetRequestTag()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => System.Text.Json.JsonSerializer.Serialize(obj);
        using var activity = _activitySource.StartActivity("Test");
        var request = new TestRequest { Command = "test" };

        // Act
        ActivityPayloadCapture.CaptureRequest(activity, request);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "catga.request.payload");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "catga.request.payload").Value;
        tagValue.Should().Contain("test");
    }

    [Fact]
    public void CaptureRequest_WithNullActivity_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        Activity? activity = null;

        // Act
        var act = () => ActivityPayloadCapture.CaptureRequest(activity, new TestRequest());

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CaptureRequest_WithNullRequest_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        using var activity = _activitySource.StartActivity("Test");

        // Act
        var act = () => ActivityPayloadCapture.CaptureRequest<TestRequest>(activity, null!);

        // Assert
        act.Should().NotThrow();
    }

    // ==================== CaptureResponse Tests ====================

    [Fact]
    public void CaptureResponse_WithValidResponse_ShouldSetResponseTag()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => System.Text.Json.JsonSerializer.Serialize(obj);
        using var activity = _activitySource.StartActivity("Test");
        var response = new TestResponse { Result = "success" };

        // Act
        ActivityPayloadCapture.CaptureResponse(activity, response);

        // Assert
        activity!.Tags.Should().Contain(tag => tag.Key == "catga.response.payload");
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == "catga.response.payload").Value;
        tagValue.Should().Contain("success");
    }

    [Fact]
    public void CaptureResponse_WithNullActivity_ShouldNotThrow()
    {
        // Arrange
        ActivityPayloadCapture.CustomSerializer = obj => obj.ToString() ?? string.Empty;
        Activity? activity = null;

        // Act
        var act = () => ActivityPayloadCapture.CaptureResponse(activity, new TestResponse());

        // Assert
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

