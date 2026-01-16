using System.Diagnostics;
using Catga.Abstractions;
using Catga.Resilience;
using Catga.Transport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Catga.Tests.Transport;

/// <summary>
/// Comprehensive tests for MessageTransportBase.
/// </summary>
public class MessageTransportBaseTests
{
    private readonly Mock<IMessageSerializer> _serializerMock;
    private readonly Mock<IResiliencePipelineProvider> _resilienceProviderMock;

    public MessageTransportBaseTests()
    {
        _serializerMock = new Mock<IMessageSerializer>();
        _resilienceProviderMock = new Mock<IResiliencePipelineProvider>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullSerializer_ShouldThrow()
    {
        var act = () => new TestMessageTransport(null!, _resilienceProviderMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_WithNullResilienceProvider_ShouldThrow()
    {
        var act = () => new TestMessageTransport(_serializerMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("provider");
    }

    [Fact]
    public void Constructor_WithPrefix_ShouldSetPrefix()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object, "myprefix");
        transport.TestGetSubject<TestMessage>().Should().StartWith("myprefix.");
    }

    [Fact]
    public void Constructor_WithPrefixEndingWithDot_ShouldNotAddExtraDot()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object, "myprefix.");
        transport.TestGetSubject<TestMessage>().Should().StartWith("myprefix.");
        transport.TestGetSubject<TestMessage>().Should().NotContain("..");
    }

    [Fact]
    public void Constructor_WithNullPrefix_ShouldUseDefault()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object, null);
        transport.TestGetSubject<TestMessage>().Should().StartWith("catga.");
    }

    [Fact]
    public void Constructor_WithCustomNaming_ShouldUseCustomNaming()
    {
        var transport = new TestMessageTransport(
            _serializerMock.Object,
            _resilienceProviderMock.Object,
            "test",
            t => $"custom.{t.Name.ToLower()}");
        transport.TestGetSubject<TestMessage>().Should().Be("test.custom.testmessage");
    }

    [Fact]
    public void Name_ShouldReturnTransportName()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        transport.Name.Should().Be("Test");
    }

    [Fact]
    public void BatchOptions_ShouldReturnNull_ByDefault()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        transport.BatchOptions.Should().BeNull();
    }

    [Fact]
    public void CompressionOptions_ShouldReturnNull_ByDefault()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        transport.CompressionOptions.Should().BeNull();
    }

    [Fact]
    public async Task PublishBatchAsync_ShouldPublishAllMessages()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        var messages = new[] { new TestMessage("1"), new TestMessage("2"), new TestMessage("3") };

        await transport.PublishBatchAsync(messages);

        transport.PublishedMessages.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendBatchAsync_ShouldSendAllMessages()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        var messages = new[] { new TestMessage("1"), new TestMessage("2") };

        await transport.SendBatchAsync(messages, "destination");

        transport.SentMessages.Should().HaveCount(2);
    }

    [Fact]
    public async Task PublishAsync_ShouldAddToPublishedMessages()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        var message = new TestMessage("test");

        await transport.PublishAsync(message);

        transport.PublishedMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_ShouldAddToSentMessages()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        var message = new TestMessage("test");

        await transport.SendAsync(message, "dest");

        transport.SentMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeAsync_ShouldRegisterHandler()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);

        await transport.SubscribeAsync<TestMessage>(async (msg, ctx) =>
        {
            await Task.CompletedTask;
        });

        transport.HasSubscription<TestMessage>().Should().BeTrue();
    }

    [Fact]
    public void GetSubject_ShouldReturnCorrectSubject()
    {
        var transport = new TestMessageTransport(_serializerMock.Object, _resilienceProviderMock.Object);
        var subject = transport.TestGetSubject<TestMessage>();
        subject.Should().StartWith("catga.");
        subject.Should().Contain("TestMessage");
    }

    public record TestMessage(string Id);

    private class TestMessageTransport : MessageTransportBase
    {
        public List<object> PublishedMessages { get; } = new();
        public List<(object Message, string Destination)> SentMessages { get; } = new();
        private readonly Dictionary<Type, object> _subscriptions = new();

        public TestMessageTransport(
            IMessageSerializer serializer,
            IResiliencePipelineProvider provider,
            string? prefix = null,
            Func<Type, string>? naming = null)
            : base(serializer, provider, prefix, naming)
        {
        }

        public override string Name => "Test";

        public string TestGetSubject<TMessage>() where TMessage : class => GetSubject<TMessage>();

        public bool HasSubscription<TMessage>() => _subscriptions.ContainsKey(typeof(TMessage));

        public override Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
        {
            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public override Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((message, destination));
            return Task.CompletedTask;
        }

        public override Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default)
        {
            _subscriptions[typeof(TMessage)] = handler;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Tests for batch queue management in MessageTransportBase.
/// </summary>
public class MessageTransportBaseBatchTests
{
    private readonly Mock<IMessageSerializer> _serializerMock;
    private readonly Mock<IResiliencePipelineProvider> _resilienceProviderMock;

    public MessageTransportBaseBatchTests()
    {
        _serializerMock = new Mock<IMessageSerializer>();
        _resilienceProviderMock = new Mock<IResiliencePipelineProvider>();
    }

    [Fact]
    public void BatchTransportOptions_ShouldBeConfigurable()
    {
        var options = new BatchTransportOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 100,
            BatchTimeout = TimeSpan.FromMilliseconds(50)
        };

        options.EnableAutoBatching.Should().BeTrue();
        options.MaxBatchSize.Should().Be(100);
        options.BatchTimeout.Should().Be(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void CompressionTransportOptions_ShouldBeConfigurable()
    {
        var options = new CompressionTransportOptions
        {
            EnableCompression = true,
            Algorithm = CompressionAlgorithm.GZip,
            MinSizeToCompress = 1024
        };

        options.EnableCompression.Should().BeTrue();
        options.Algorithm.Should().Be(CompressionAlgorithm.GZip);
        options.MinSizeToCompress.Should().Be(1024);
    }

    public record BatchTestMessage(string Id);

    private class BatchingTestTransport : MessageTransportBase
    {
        private readonly BatchTransportOptions? _batchOptions;
        public List<object> ProcessedBatches { get; } = new();

        public BatchingTestTransport(
            IMessageSerializer serializer,
            IResiliencePipelineProvider provider,
            BatchTransportOptions? batchOptions = null)
            : base(serializer, provider)
        {
            _batchOptions = batchOptions;
            if (batchOptions != null)
                InitializeBatchTimer(batchOptions);
        }

        public override string Name => "BatchTest";
        public override BatchTransportOptions? BatchOptions => _batchOptions;

        public override Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        protected override Task ProcessBatchItemsAsync(List<BatchItem> items, Activity? batchSpan)
        {
            ProcessedBatches.Add(items);
            return Task.CompletedTask;
        }
    }
}
