using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Persistence;
using Catga.Persistence.Nats;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Persistence;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using NATS.Client.Core;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class AdditionalE2ETests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _nats;
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        // NATS with JetStream
        _natsContainer = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/varz")))
            .Build();
        await _natsContainer.StartAsync();
        var natsPort = _natsContainer.GetMappedPublicPort(4222);
        _nats = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{natsPort}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await _nats.ConnectAsync();

        // Redis 7
        _redisContainer = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_nats is not null) await _nats.DisposeAsync();
        _redis?.Dispose();
        if (_natsContainer is not null) await _natsContainer.DisposeAsync();
        if (_redisContainer is not null) await _redisContainer.DisposeAsync();
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
    public async Task Redis_DLQ_Send_And_ReadBack()
    {
        if (_redis is null) return;
        var dlq = new RedisDeadLetterQueue(_redis, _serializer);
        var msg = new DlqMessage { MessageId = MessageExtensions.NewMessageId(), Id = "r-dlq-1", Data = "boom" };

        await dlq.SendAsync(msg, new InvalidOperationException("boom"), retryCount: 2);
        await Task.Delay(200);

        var items = await dlq.GetFailedMessagesAsync(10);
        items.Should().NotBeNull();
        items.Should().NotBeEmpty();
        items.Should().Contain(x => x.MessageId == msg.MessageId && x.RetryCount == 2);
    }

    [Fact]
    public async Task NATS_DLQ_Send_And_ReadBack()
    {
        if (_nats is null) return;
        var provider = new DiagnosticResiliencePipelineProvider();
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, streamName: "CATGA_DLQ_E2E", options: null, provider: provider);
        var msg = new DlqMessage { MessageId = MessageExtensions.NewMessageId(), Id = "n-dlq-1", Data = "boom" };

        await dlq.SendAsync(msg, new InvalidOperationException("boom"), retryCount: 1);
        await Task.Delay(300);

        var items = await dlq.GetFailedMessagesAsync(10);
        items.Should().NotBeNull();
        items.Should().NotBeEmpty();
        items.Should().Contain(x => x.MessageId == msg.MessageId && x.RetryCount >= 1);
    }

    [Fact]
    public async Task Redis_Idempotency_Mark_And_Get()
    {
        if (_redis is null) return;
        var provider = new DiagnosticResiliencePipelineProvider();
        var store = new RedisIdempotencyStore(_redis, _serializer, Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisIdempotencyStore>.Instance, options: null, provider: provider);

        var id = MessageExtensions.NewMessageId();
        var result = new IdemResult { Value = "ok" };
        await store.MarkAsProcessedAsync(id, result);
        await Task.Delay(100);

        var exists = await store.HasBeenProcessedAsync(id);
        exists.Should().BeTrue();

        var cached = await store.GetCachedResultAsync<IdemResult>(id);
        cached.Should().NotBeNull();
        cached!.Value.Should().Be("ok");
    }

    [Fact]
    public async Task NATS_Idempotency_Mark_And_Get()
    {
        if (_nats is null) return;
        var provider = new DiagnosticResiliencePipelineProvider();
        var store = new NatsJSIdempotencyStore(_nats, _serializer, streamName: "CATGA_IDEMPOTENCY_E2E", consumerName: null, options: null, provider: provider);

        var id = MessageExtensions.NewMessageId();
        var result = new IdemResult { Value = "ok-nats" };
        await store.MarkAsProcessedAsync(id, result);
        await Task.Delay(200);

        var exists = await store.HasBeenProcessedAsync(id);
        exists.Should().BeTrue();

        var cached = await store.GetCachedResultAsync<IdemResult>(id);
        cached.Should().NotBeNull();
        cached!.Value.Should().Be("ok-nats");
    }

    [Fact]
    public async Task Redis_Transport_SendBatch_To_Stream()
    {
        if (_redis is null) return;
        var provider = new DiagnosticResiliencePipelineProvider();
        await using var transport = new RedisMessageTransport(_redis, _serializer, provider: provider);

        var destination = "worker-batch";
        var events = Enumerable.Range(0, 10).Select(i => new DlqMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = $"b-{i}",
            Data = "stream"
        }).ToList();

        await transport.SendBatchAsync(events, destination);
        await Task.Delay(300);

        var key = $"stream:{destination}";
        var entries = await _redis.GetDatabase().StreamReadAsync(key, "0-0");
        entries.Should().NotBeNull();
        entries.Length.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public async Task Redis_E2E_Outbox_To_Transport_To_Inbox_QoS1_AtLeastOnce()
    {
        if (_redis is null) return;
        var provider = new DiagnosticResiliencePipelineProvider();
        await using var transport = new RedisMessageTransport(
            _redis,
            _serializer,
            provider: provider);
        var outbox = new RedisOutboxPersistence(
            _redis,
            _serializer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisOutboxPersistence>.Instance,
            options: null,
            provider: provider);
        var inbox = new RedisInboxPersistence(
            _redis,
            _serializer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisInboxPersistence>.Instance,
            options: null,
            provider: provider);

        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<DlqMessage>(async (msg, ctx) =>
        {
            var id = msg.MessageId;
            if (!await inbox.TryLockMessageAsync(id, TimeSpan.FromMinutes(5))) return;
            Interlocked.Increment(ref received);
            await inbox.MarkAsProcessedAsync(new InboxMessage
            {
                MessageId = id,
                MessageType = typeof(DlqMessage).FullName!,
                Payload = Convert.ToBase64String(_serializer.Serialize(msg, typeof(DlqMessage))),
                Status = InboxStatus.Processed,
                ReceivedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow,
                ProcessingResult = "ok"
            });
            tcs.TrySetResult();
        });

        await Task.Delay(150);

        var messageId = MessageExtensions.NewMessageId();
        var ev = new DlqMessage { MessageId = messageId, Id = "redis-e2e-1", Data = "hello" };

        var outboxMsg = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(DlqMessage).FullName!,
            Payload = Convert.ToBase64String(_serializer.Serialize(ev, typeof(DlqMessage))),
            Status = OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await outbox.AddAsync(outboxMsg);

        // QoS1 via Redis Streams path goes through SendAsync (point-to-point)
        await transport.SendAsync(ev, destination: "e2e-redis-inbox");
        await outbox.MarkAsPublishedAsync(messageId);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        done.Should().Be(tcs.Task);
        Volatile.Read(ref received).Should().Be(1);

        var has = await inbox.HasBeenProcessedAsync(messageId);
        has.Should().BeTrue();
    }

    [MemoryPackable]
    private partial record DlqMessage : IMessage
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
    }

    [MemoryPackable]
    private partial record IdemResult
    {
        public required string Value { get; init; }
    }
}
