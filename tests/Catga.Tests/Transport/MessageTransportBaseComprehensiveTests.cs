using Catga.Abstractions;
using Catga.Core;
using Catga.Resilience;
using Catga.Transport;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Transport;

/// <summary>
/// Comprehensive tests for MessageTransportBase
/// </summary>
public class MessageTransportBaseComprehensiveTests
{
    #region Test Types

    public record TestMessage(string Data) : IMessage
    {
        public long MessageId { get; init; }
    }

    public class TestMessageTransport : MessageTransportBase
    {
        public override string Name => "Test";
        public List<(object Message, TransportContext? Context)> PublishedMessages { get; } = [];
        public List<(object Message, string Destination, TransportContext? Context)> SentMessages { get; } = [];
        public List<Type> SubscribedTypes { get; } = [];

        public TestMessageTransport(IMessageSerializer serializer, IResiliencePipelineProvider provider, string? prefix = null)
            : base(serializer, provider, prefix)
        {
        }

        public override Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
        {
            PublishedMessages.Add((message!, context));
            return Task.CompletedTask;
        }

        public override Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((message!, destination, context));
            return Task.CompletedTask;
        }

        public override Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default)
        {
            SubscribedTypes.Add(typeof(TMessage));
            return Task.CompletedTask;
        }

        public string GetSubjectForTest<TMessage>() where TMessage : class => GetSubject<TMessage>();
    }

    #endregion

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();

        var transport = new TestMessageTransport(serializer, provider);

        transport.Should().NotBeNull();
        transport.Name.Should().Be("Test");
    }

    [Fact]
    public void Constructor_WithNullSerializer_ShouldThrow()
    {
        var provider = Substitute.For<IResiliencePipelineProvider>();

        var act = () => new TestMessageTransport(null!, provider);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_WithNullProvider_ShouldThrow()
    {
        var serializer = Substitute.For<IMessageSerializer>();

        var act = () => new TestMessageTransport(serializer, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("provider");
    }

    [Fact]
    public void Constructor_WithPrefix_ShouldSetPrefix()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();

        var transport = new TestMessageTransport(serializer, provider, "myapp");

        var subject = transport.GetSubjectForTest<TestMessage>();
        subject.Should().StartWith("myapp.");
    }

    [Fact]
    public void Constructor_WithPrefixEndingWithDot_ShouldNotAddExtraDot()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();

        var transport = new TestMessageTransport(serializer, provider, "myapp.");

        var subject = transport.GetSubjectForTest<TestMessage>();
        subject.Should().StartWith("myapp.");
        subject.Should().NotContain("..");
    }

    [Fact]
    public void Constructor_WithNullPrefix_ShouldUseDefault()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();

        var transport = new TestMessageTransport(serializer, provider, null);

        var subject = transport.GetSubjectForTest<TestMessage>();
        subject.Should().StartWith("catga.");
    }

    [Fact]
    public async Task PublishAsync_ShouldPublishMessage()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);
        var message = new TestMessage("test");

        await transport.PublishAsync(message);

        transport.PublishedMessages.Should().HaveCount(1);
        transport.PublishedMessages[0].Message.Should().Be(message);
    }

    [Fact]
    public async Task SendAsync_ShouldSendMessage()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);
        var message = new TestMessage("test");

        await transport.SendAsync(message, "destination");

        transport.SentMessages.Should().HaveCount(1);
        transport.SentMessages[0].Message.Should().Be(message);
        transport.SentMessages[0].Destination.Should().Be("destination");
    }

    [Fact]
    public async Task SubscribeAsync_ShouldRegisterSubscription()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);

        await transport.SubscribeAsync<TestMessage>((msg, ctx) => Task.CompletedTask);

        transport.SubscribedTypes.Should().Contain(typeof(TestMessage));
    }

    [Fact]
    public async Task PublishBatchAsync_ShouldPublishAllMessages()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);
        var messages = new[]
        {
            new TestMessage("test1"),
            new TestMessage("test2"),
            new TestMessage("test3")
        };

        await transport.PublishBatchAsync(messages);

        transport.PublishedMessages.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendBatchAsync_ShouldSendAllMessages()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);
        var messages = new[]
        {
            new TestMessage("test1"),
            new TestMessage("test2")
        };

        await transport.SendBatchAsync(messages, "destination");

        transport.SentMessages.Should().HaveCount(2);
        transport.SentMessages.Should().AllSatisfy(m => m.Destination.Should().Be("destination"));
    }

    [Fact]
    public void GetSubject_ShouldReturnCorrectSubject()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider, "test");

        var subject = transport.GetSubjectForTest<TestMessage>();

        subject.Should().Be("test.TestMessage");
    }

    [Fact]
    public void BatchOptions_ShouldReturnNull()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);

        transport.BatchOptions.Should().BeNull();
    }

    [Fact]
    public void CompressionOptions_ShouldReturnNull()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var provider = Substitute.For<IResiliencePipelineProvider>();
        var transport = new TestMessageTransport(serializer, provider);

        transport.CompressionOptions.Should().BeNull();
    }
}
