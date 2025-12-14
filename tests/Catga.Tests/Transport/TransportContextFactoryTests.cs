using Catga.Transport;
using FluentAssertions;

namespace Catga.Tests.Transport;

public class TransportContextFactoryTests
{
    public record TestMessage(string Data);
    public record AnotherMessage(int Value);

    [Fact]
    public void CreateDefault_GeneratesMessageId()
    {
        var context = TransportContextFactory.CreateDefault<TestMessage>();

        context.MessageId.Should().NotBeNull();
        context.MessageId.Should().NotBe(0);
    }

    [Fact]
    public void CreateDefault_SetsMessageType()
    {
        var context = TransportContextFactory.CreateDefault<TestMessage>();

        context.MessageType.Should().Contain("TestMessage");
    }

    [Fact]
    public void CreateDefault_SetsSentAt()
    {
        var before = DateTime.UtcNow;
        var context = TransportContextFactory.CreateDefault<TestMessage>();
        var after = DateTime.UtcNow;

        context.SentAt.Should().NotBeNull();
        context.SentAt!.Value.Should().BeOnOrAfter(before);
        context.SentAt!.Value.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void CreateDefault_DifferentTypes_DifferentMessageTypes()
    {
        var context1 = TransportContextFactory.CreateDefault<TestMessage>();
        var context2 = TransportContextFactory.CreateDefault<AnotherMessage>();

        context1.MessageType.Should().NotBe(context2.MessageType);
    }

    [Fact]
    public void CreateDefault_MultipleCallsGenerateUniqueIds()
    {
        var context1 = TransportContextFactory.CreateDefault<TestMessage>();
        var context2 = TransportContextFactory.CreateDefault<TestMessage>();

        context1.MessageId.Should().NotBe(context2.MessageId);
    }

    [Fact]
    public void GetOrCreate_WithNull_CreatesNew()
    {
        var context = TransportContextFactory.GetOrCreate<TestMessage>(null);

        context.MessageId.Should().NotBeNull();
        context.MessageType.Should().Contain("TestMessage");
    }

    [Fact]
    public void GetOrCreate_WithExisting_ReturnsExisting()
    {
        var existing = new TransportContext
        {
            MessageId = 12345,
            MessageType = "Custom",
            SentAt = DateTime.UtcNow
        };

        var context = TransportContextFactory.GetOrCreate<TestMessage>(existing);

        context.Should().Be(existing);
        context.MessageId.Should().Be(12345);
        context.MessageType.Should().Be("Custom");
    }

    [Fact]
    public void CreateDefault_RetryCountIsZero()
    {
        var context = TransportContextFactory.CreateDefault<TestMessage>();

        context.RetryCount.Should().Be(0);
    }

    [Fact]
    public void CreateDefault_MetadataIsNull()
    {
        var context = TransportContextFactory.CreateDefault<TestMessage>();

        context.Metadata.Should().BeNull();
    }

    [Fact]
    public void CreateDefault_CorrelationIdIsNull()
    {
        var context = TransportContextFactory.CreateDefault<TestMessage>();

        context.CorrelationId.Should().BeNull();
    }
}
