using Catga.Transport;
using FluentAssertions;
using Catga.Abstractions;

namespace Catga.Tests.Transport;

public class TransportMessageContextAccessorTests
{
    private sealed class TestMessage
    {
        public long MessageId { get; set; }
        public long? CorrelationId { get; set; }
    }

    private sealed class NonNullableCorrelationMessage
    {
        public long MessageId { get; set; }
        public long CorrelationId { get; set; }
    }

    private sealed class PrioritizedOutgoingMessage : IPrioritizedMessage
    {
        public long MessageId { get; init; }
        public MessagePriority Priority { get; init; } = MessagePriority.Normal;
    }

    private sealed class DelayedOutgoingMessage : IDelayedMessage
    {
        public long MessageId { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public TimeSpan? Delay { get; init; }
    }

    private sealed record OutgoingMessage(string Value);

    [Fact]
    public void EnrichOutgoing_ShouldInheritAmbientCorrelationId_WhenExplicitCorrelationIdIsMissing()
    {
        using var scope = TransportMessageContextAccessor.Push(new TransportContext
        {
            CorrelationId = 123L
        });

        var context = TransportMessageContextAccessor.EnrichOutgoing<OutgoingMessage>(new TransportContext
        {
            MessageType = "custom"
        });

        context.CorrelationId.Should().Be(123L);
    }

    [Fact]
    public void EnrichOutgoing_ShouldMergeAmbientMetadata_AndAllowExplicitKeysToOverride()
    {
        using var scope = TransportMessageContextAccessor.Push(new TransportContext
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reply_to"] = "ambient-reply",
                ["shared"] = "ambient-value"
            }
        });

        var context = TransportMessageContextAccessor.EnrichOutgoing<OutgoingMessage>(new TransportContext
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["shared"] = "explicit-value",
                ["custom"] = "explicit-custom"
            }
        });

        context.Metadata.Should().NotBeNull();
        context.Metadata.Should().Contain(new KeyValuePair<string, string>("reply_to", "ambient-reply"));
        context.Metadata.Should().Contain(new KeyValuePair<string, string>("shared", "explicit-value"));
        context.Metadata.Should().Contain(new KeyValuePair<string, string>("custom", "explicit-custom"));
    }

    [Fact]
    public void EnrichOutgoing_ShouldAddPriorityMetadata_WhenMessageImplementsIPrioritizedMessage()
    {
        var message = new PrioritizedOutgoingMessage
        {
            Priority = MessagePriority.Critical
        };

        var context = TransportMessageContextAccessor.EnrichOutgoing(message, null);

        context.Metadata.Should().NotBeNull();
        context.Metadata!["x-priority"].Should().Be("3");
    }

    [Fact]
    public void EnrichOutgoing_ShouldNotOverrideExplicitPriorityMetadata_WhenMessageImplementsIPrioritizedMessage()
    {
        var message = new PrioritizedOutgoingMessage
        {
            Priority = MessagePriority.Critical
        };

        var context = TransportMessageContextAccessor.EnrichOutgoing(
            message,
            new TransportContext
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-priority"] = "1"
                }
            });

        context.Metadata.Should().NotBeNull();
        context.Metadata!["x-priority"].Should().Be("1");
    }

    [Fact]
    public void EnrichOutgoing_ShouldAddDelayMetadata_WhenMessageImplementsIDelayedMessage()
    {
        var message = new DelayedOutgoingMessage
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(5)
        };

        var context = TransportMessageContextAccessor.EnrichOutgoing(message, null);

        context.Metadata.Should().NotBeNull();
        context.Metadata!.Should().ContainKey("x-delay");
        int.Parse(context.Metadata["x-delay"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public void EnrichOutgoing_ShouldNotOverrideExplicitDelayMetadata_WhenMessageImplementsIDelayedMessage()
    {
        var message = new DelayedOutgoingMessage
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(5)
        };

        var context = TransportMessageContextAccessor.EnrichOutgoing(
            message,
            new TransportContext
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-delay"] = "123"
                }
            });

        context.Metadata.Should().NotBeNull();
        context.Metadata!["x-delay"].Should().Be("123");
    }

    [Fact]
    public void Push_ShouldRestorePreviousScope_WhenNestedScopeIsDisposed()
    {
        using var outer = TransportMessageContextAccessor.Push(new TransportContext
        {
            CorrelationId = 11L,
            Metadata = new Dictionary<string, string> { ["scope"] = "outer" }
        });

        using (TransportMessageContextAccessor.Push(new TransportContext
               {
                   CorrelationId = 22L,
                   Metadata = new Dictionary<string, string> { ["scope"] = "inner" }
               }))
        {
            TransportMessageContextAccessor.Current.Should().NotBeNull();
            TransportMessageContextAccessor.Current!.Value.CorrelationId.Should().Be(22L);
            TransportMessageContextAccessor.Current!.Value.Metadata!["scope"].Should().Be("inner");
        }

        TransportMessageContextAccessor.Current.Should().NotBeNull();
        TransportMessageContextAccessor.Current!.Value.CorrelationId.Should().Be(11L);
        TransportMessageContextAccessor.Current!.Value.Metadata!["scope"].Should().Be("outer");
    }

    [Fact]
    public void Push_ShouldNotLeakAmbientContext_AfterScopeIsDisposed()
    {
        using (TransportMessageContextAccessor.Push(new TransportContext
               {
                   CorrelationId = 999L,
                   Metadata = new Dictionary<string, string> { ["reply_to"] = "inbox" }
               }))
        {
            TransportMessageContextAccessor.Current.Should().NotBeNull();
        }

        TransportMessageContextAccessor.Current.Should().BeNull();

        var context = TransportMessageContextAccessor.EnrichOutgoing<OutgoingMessage>(new TransportContext());
        context.CorrelationId.Should().BeNull();
        context.Metadata.Should().BeNull();
    }

    [Fact]
    public void ApplyToMessage_ShouldPopulateUnsetMessageAndCorrelationIds()
    {
        var message = new TestMessage();

        TransportMessageContextAccessor.ApplyToMessage(message, new TransportContext
        {
            MessageId = 456L,
            CorrelationId = 789L
        });

        message.MessageId.Should().Be(456L);
        message.CorrelationId.Should().Be(789L);
        TransportMessageContextAccessor.TryGetMessageId(message).Should().Be(456L);
        TransportMessageContextAccessor.TryGetCorrelationId(message).Should().Be(789L);
    }

    [Fact]
    public void ApplyToMessage_ShouldNotOverwriteExistingIds()
    {
        var message = new NonNullableCorrelationMessage
        {
            MessageId = 100L,
            CorrelationId = 200L
        };

        TransportMessageContextAccessor.ApplyToMessage(message, new TransportContext
        {
            MessageId = 300L,
            CorrelationId = 400L
        });

        message.MessageId.Should().Be(100L);
        message.CorrelationId.Should().Be(200L);
    }
}
