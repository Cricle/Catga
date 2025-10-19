using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Messages;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Catga.Serialization.Json;

namespace Catga.Benchmarks;

/// <summary>
/// 持久化性能测试
/// 测试 Outbox, Inbox, EventStore 的读写性能
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PersistenceBenchmarks
{
    private MemoryOutboxStore? _outboxStore;
    private MemoryInboxStore? _inboxStore;
    private InMemoryEventStore? _eventStore;
    private IMessageSerializer? _serializer;
    private OutboxMessage? _outboxMessage;
    private InboxMessage? _inboxMessage;
    private List<IEvent>? _events;
    private string? _streamId;

    [GlobalSetup]
    public void Setup()
    {
        _serializer = new JsonMessageSerializer();
        _outboxStore = new MemoryOutboxStore();
        _inboxStore = new MemoryInboxStore();
        _eventStore = new InMemoryEventStore();

        var testEvent = new TestEvent
        {
            MessageId = Guid.NewGuid().ToString(),
            Id = "test-1",
            Data = "Test data"
        };

        _outboxMessage = new OutboxMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer.Serialize(testEvent)),
            Status = OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _inboxMessage = new InboxMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer.Serialize(testEvent)),
            Status = InboxStatus.Processing,
            ReceivedAt = DateTime.UtcNow
        };

        _events = Enumerable.Range(0, 10)
            .Select(i => new TestEvent
            {
                MessageId = Guid.NewGuid().ToString(),
                Id = $"event-{i}",
                Data = $"Event data {i}"
            } as IEvent)
            .ToList();

        _streamId = $"stream-{Guid.NewGuid()}";
    }

    #region Outbox Benchmarks

    [Benchmark(Description = "Outbox: Add Message")]
    public async Task Outbox_AddMessage()
    {
        await _outboxStore!.AddAsync(_outboxMessage!);
    }

    [Benchmark(Description = "Outbox: Get Pending (10)")]
    public async Task Outbox_GetPending()
    {
        // Pre-populate
        for (int i = 0; i < 10; i++)
        {
            var msg = new OutboxMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                MessageType = "Test",
                Payload = "Data",
                Status = OutboxStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            await _outboxStore!.AddAsync(msg);
        }

        await _outboxStore!.GetPendingMessagesAsync(10);
    }

    [Benchmark(Description = "Outbox: Mark Published")]
    public async Task Outbox_MarkPublished()
    {
        var msg = new OutboxMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = "Test",
            Payload = "Data",
            Status = OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await _outboxStore!.AddAsync(msg);
        await _outboxStore.MarkAsPublishedAsync(msg.MessageId);
    }

    #endregion

    #region Inbox Benchmarks

    [Benchmark(Description = "Inbox: Lock Message")]
    public async Task Inbox_LockMessage()
    {
        var messageId = Guid.NewGuid().ToString();
        await _inboxStore!.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
    }

    [Benchmark(Description = "Inbox: Mark Processed")]
    public async Task Inbox_MarkProcessed()
    {
        var msg = new InboxMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = "Test",
            Payload = "Data",
            Status = InboxStatus.Processing,
            ReceivedAt = DateTime.UtcNow
        };
        await _inboxStore!.MarkAsProcessedAsync(msg);
    }

    [Benchmark(Description = "Inbox: Check Processed")]
    public async Task Inbox_CheckProcessed()
    {
        var messageId = Guid.NewGuid().ToString();
        var msg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "Test",
            Payload = "Data",
            Status = InboxStatus.Processed,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };
        await _inboxStore!.MarkAsProcessedAsync(msg);
        await _inboxStore.HasBeenProcessedAsync(messageId);
    }

    #endregion

    #region EventStore Benchmarks

    [Benchmark(Description = "EventStore: Append 10 Events")]
    public async Task EventStore_Append10()
    {
        var streamId = $"stream-{Guid.NewGuid()}";
        await _eventStore!.AppendAsync(streamId, _events!);
    }

    [Benchmark(Description = "EventStore: Read Stream")]
    public async Task EventStore_Read()
    {
        // Pre-populate
        var streamId = $"stream-{Guid.NewGuid()}";
        await _eventStore!.AppendAsync(streamId, _events!);

        await _eventStore.ReadAsync(streamId);
    }

    [Benchmark(Description = "EventStore: Get Version")]
    public async Task EventStore_GetVersion()
    {
        // Pre-populate
        var streamId = $"stream-{Guid.NewGuid()}";
        await _eventStore!.AppendAsync(streamId, _events!);

        await _eventStore.GetVersionAsync(streamId);
    }

    [Benchmark(Description = "EventStore: Optimistic Concurrency")]
    public async Task EventStore_OptimisticConcurrency()
    {
        var streamId = $"stream-{Guid.NewGuid()}";
        
        // First append
        await _eventStore!.AppendAsync(streamId, _events!.Take(5).ToList());
        
        // Get version
        var version = await _eventStore.GetVersionAsync(streamId);
        
        // Append with expected version
        await _eventStore.AppendAsync(streamId, _events.Skip(5).Take(5).ToList(), version);
    }

    #endregion

    #region Test Models

    public record TestEvent : IEvent
    {
        public required string MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    #endregion
}

