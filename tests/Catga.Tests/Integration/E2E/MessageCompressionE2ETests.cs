using Catga.Abstractions;
using Catga.Core;
using Catga.Persistence.Redis.Persistence;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed partial class MessageCompressionE2ETests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        if (_redisContainer is not null)
            await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public void MessageCompressor_GZip_CompressAndDecompress()
    {
        // Create large payload to benefit from compression
        var largeData = new string('A', 10000);
        var message = new LargeMessage { MessageId = MessageExtensions.NewMessageId(), Data = largeData };
        var serialized = _serializer.Serialize(message);

        // Compress
        var compressed = MessageCompressor.Compress(serialized, CompressionAlgorithm.GZip);
        compressed.Length.Should().BeLessThan(serialized.Length);

        // Decompress
        var decompressed = MessageCompressor.Decompress(compressed);
        decompressed.Should().BeEquivalentTo(serialized);

        // Deserialize and verify
        var restored = _serializer.Deserialize<LargeMessage>(decompressed);
        restored.Should().NotBeNull();
        restored!.Data.Should().Be(largeData);
    }

    [Fact]
    public void MessageCompressor_Brotli_CompressAndDecompress()
    {
        var largeData = new string('B', 10000);
        var message = new LargeMessage { MessageId = MessageExtensions.NewMessageId(), Data = largeData };
        var serialized = _serializer.Serialize(message);

        var compressed = MessageCompressor.Compress(serialized, CompressionAlgorithm.Brotli);
        compressed.Length.Should().BeLessThan(serialized.Length);

        var decompressed = MessageCompressor.Decompress(compressed);
        decompressed.Should().BeEquivalentTo(serialized);

        var restored = _serializer.Deserialize<LargeMessage>(decompressed);
        restored.Should().NotBeNull();
        restored!.Data.Should().Be(largeData);
    }

    [Fact]
    public void MessageCompressor_Deflate_CompressAndDecompress()
    {
        var largeData = new string('C', 10000);
        var message = new LargeMessage { MessageId = MessageExtensions.NewMessageId(), Data = largeData };
        var serialized = _serializer.Serialize(message);

        var compressed = MessageCompressor.Compress(serialized, CompressionAlgorithm.Deflate);
        compressed.Length.Should().BeLessThan(serialized.Length);

        var decompressed = MessageCompressor.Decompress(compressed);
        decompressed.Should().BeEquivalentTo(serialized);

        var restored = _serializer.Deserialize<LargeMessage>(decompressed);
        restored.Should().NotBeNull();
        restored!.Data.Should().Be(largeData);
    }

    [Fact]
    public async Task Redis_Transport_WithCompression_PublishAndSubscribe()
    {
        if (_redis is null) return;

        var provider = new DiagnosticResiliencePipelineProvider();
        await using var transport = new RedisMessageTransport(_redis, _serializer, provider: provider);

        var tcs = new TaskCompletionSource<LargeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<LargeMessage>((msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            return Task.CompletedTask;
        });

        await Task.Delay(150);

        // Create and compress message
        var largeData = new string('D', 5000);
        var message = new LargeMessage { MessageId = MessageExtensions.NewMessageId(), Data = largeData };

        // Publish (transport handles serialization internally)
        await transport.PublishAsync(message);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        done.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.Data.Should().Be(largeData);
    }

    [Fact]
    public async Task Redis_Outbox_WithCompressedPayload()
    {
        if (_redis is null) return;

        var provider = new DiagnosticResiliencePipelineProvider();
        var outbox = new RedisOutboxPersistence(
            _redis,
            _serializer,
            NullLogger<RedisOutboxPersistence>.Instance,
            options: null,
            provider: provider);

        var largeData = new string('E', 8000);
        var message = new LargeMessage { MessageId = MessageExtensions.NewMessageId(), Data = largeData };
        var serialized = _serializer.Serialize(message);
        var compressed = MessageCompressor.Compress(serialized, CompressionAlgorithm.GZip);

        var outboxMsg = new Catga.Outbox.OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = typeof(LargeMessage).FullName!,
            Payload = compressed, // Store compressed
            Status = Catga.Outbox.OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await outbox.AddAsync(outboxMsg);

        // Verify message was added
        outboxMsg.MessageId.Should().BeGreaterThan(0);

        // Decompress and verify
        var decompressed = MessageCompressor.Decompress(compressed);
        var restored = _serializer.Deserialize<LargeMessage>(decompressed);

        restored.Should().NotBeNull();
        restored!.Data.Should().Be(largeData);
    }

    [Fact]
    public void MessageCompressor_EstimateCompressionRatio()
    {
        // Highly compressible data
        var repetitive = new string('X', 10000);
        var original = System.Text.Encoding.UTF8.GetBytes(repetitive);
        var compressed = MessageCompressor.Compress(original, CompressionAlgorithm.GZip);
        var ratio = MessageCompressor.EstimateCompressionRatio(original, compressed);
        ratio.Should().BeLessThan(0.2); // Should compress very well

        // Random data (less compressible)
        var random = new byte[1000];
        new Random(42).NextBytes(random);
        var randomCompressed = MessageCompressor.Compress(random, CompressionAlgorithm.GZip);
        var randomRatio = MessageCompressor.EstimateCompressionRatio(random, randomCompressed);
        randomRatio.Should().BeGreaterThan(0.5); // Random data doesn't compress well
    }

    [MemoryPackable]
    private partial record LargeMessage : IMessage
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }
}



