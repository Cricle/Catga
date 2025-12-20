// AOT Validation - Comprehensive runtime validation for all Catga libraries
// Usage:
//   dotnet run -- inmemory     # Test InMemory only (default)
//   dotnet run -- redis        # Test Redis (requires Redis server)
//   dotnet run -- nats         # Test NATS (requires NATS server)
//   dotnet run -- all          # Test all backends
//   dotnet run -- mixed        # Test mixed backend scenarios

using System.Text.Json;
using System.Text.Json.Serialization;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Idempotency;
using Catga.Persistence.InMemory;
using Catga.Persistence.Redis;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Catga.Transport.Nats;
using Catga.Transport.Redis;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MemoryPack;
using NATS.Client.Core;
using StackExchange.Redis;

#pragma warning disable CS1591

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "inmemory";
var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║        Catga AOT Validation - Comprehensive Tests            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  Mode: {mode} | Redis: {redisConnection} | NATS: {natsUrl}");
Console.WriteLine();

var t = new AotTester();

// ============================================================================
// 1. Core Types
// ============================================================================
t.Section("1. Core Types");
await t.TestAsync("IdGenerator.NewBase64Id()", () => !string.IsNullOrEmpty(IdGenerator.NewBase64Id()));
await t.TestAsync("CatgaResult.Success()", () => CatgaResult.Success().IsSuccess);
await t.TestAsync("CatgaResult<T>.Success()", () => {
    var r = CatgaResult<int>.Success(42);
    return r.IsSuccess && r.Value == 42;
});
await t.TestAsync("CatgaResult.Failure()", () => {
    var r = CatgaResult.Failure("err");
    return !r.IsSuccess && r.Error == "err";
});
await t.TestAsync("ErrorInfo creation", () => {
    var err = new ErrorInfo { Code = "CODE", Message = "Msg" };
    return err.Code == "CODE";
});

// ============================================================================
// 2. Distributed ID
// ============================================================================
t.Section("2. Distributed ID");
await t.TestAsync("SnowflakeIdGenerator.NextId()", () => {
    var gen = new SnowflakeIdGenerator(1);
    return gen.NextId() > 0 && gen.NextId() > 0;
});
await t.TestAsync("MessageId.NewId()", () => {
    var gen = new SnowflakeIdGenerator(1);
    return MessageId.NewId(gen).Value > 0;
});
await t.TestAsync("CorrelationId.NewId()", () => {
    var gen = new SnowflakeIdGenerator(1);
    return CorrelationId.NewId(gen).Value > 0;
});
await t.TestAsync("MessageId equality", () => {
    var id1 = new MessageId(12345);
    var id2 = new MessageId(12345);
    return id1 == id2;
});

// ============================================================================
// 3. Serialization
// ============================================================================
t.Section("3. Serialization");
await t.TestAsync("MemoryPack serialize/deserialize", () => {
    var msg = new SerializableMessage { Id = 123, Name = "Test" };
    var bytes = MemoryPackSerializer.Serialize(msg);
    var result = MemoryPackSerializer.Deserialize<SerializableMessage>(bytes);
    return result?.Id == 123 && result.Name == "Test";
});
await t.TestAsync("MemoryPack complex types", () => {
    var msg = new ComplexMessage { 
        Items = new List<string> { "a", "b" },
        Metadata = new Dictionary<string, int> { ["x"] = 1 }
    };
    var bytes = MemoryPackSerializer.Serialize(msg);
    var result = MemoryPackSerializer.Deserialize<ComplexMessage>(bytes);
    return result?.Items?.Count == 2 && result.Metadata?["x"] == 1;
});
await t.TestAsync("System.Text.Json AOT", () => {
    var obj = new TestJsonObject { Id = 1, Name = "Test" };
    var json = JsonSerializer.Serialize(obj, AotJsonContext.Default.TestJsonObject);
    var result = JsonSerializer.Deserialize(json, AotJsonContext.Default.TestJsonObject);
    return result?.Id == 1;
});

// ============================================================================
// 4. Message Types & Attributes
// ============================================================================
t.Section("4. Message Types & Attributes");
await t.TestAsync("QueryBase<T>", () => new TestQuery { Name = "test" }.Name == "test");
await t.TestAsync("CommandBase", () => new TestCommand { Name = "test" }.Name == "test");
await t.TestAsync("EventBase", () => new TestEvent { Data = "test" }.Data == "test");
await t.TestAsync("IDelayedMessage", () => {
    var msg = new DelayedTestEvent { Data = "test", DeliverAt = DateTimeOffset.UtcNow.AddMinutes(5) };
    return msg.DeliverAt > DateTimeOffset.UtcNow;
});
await t.TestAsync("IPrioritizedMessage", () => {
    var msg = new PrioritizedTestEvent { Data = "test", Priority = MessagePriority.High };
    return msg.Priority == MessagePriority.High;
});
await t.TestAsync("RetryAttribute", () => {
    var attr = new RetryAttribute { MaxAttempts = 5, DelayMs = 200 };
    return attr.MaxAttempts == 5 && attr.DelayMs == 200;
});
await t.TestAsync("TimeoutAttribute", () => new TimeoutAttribute(30).Seconds == 30);
await t.TestAsync("CircuitBreakerAttribute", () => {
    var attr = new CircuitBreakerAttribute { FailureThreshold = 10 };
    return attr.FailureThreshold == 10;
});
await t.TestAsync("IdempotentAttribute", () => new IdempotentAttribute { TtlSeconds = 3600 }.TtlSeconds == 3600);
await t.TestAsync("DistributedLockAttribute", () => new DistributedLockAttribute("key").Key == "key");
await t.TestAsync("ShardedAttribute", () => new ShardedAttribute("ShardKey").Key == "ShardKey");
await t.TestAsync("BroadcastAttribute", () => new BroadcastAttribute() != null);
await t.TestAsync("LeaderOnlyAttribute", () => new LeaderOnlyAttribute() != null);
await t.TestAsync("ClusterSingletonAttribute", () => new ClusterSingletonAttribute() != null);

// ============================================================================
// 5. Flow DSL
// ============================================================================
t.Section("5. Flow DSL");
await t.TestAsync("DslFlowStatus enum", () => Enum.IsDefined(typeof(DslFlowStatus), DslFlowStatus.Running));
await t.TestAsync("FlowStatus enum", () => Enum.IsDefined(typeof(FlowStatus), FlowStatus.Running));
await t.TestAsync("FlowState creation", () => {
    var state = new FlowState { Id = "f1", Type = "TestFlow", Status = FlowStatus.Running };
    return state.Id == "f1" && state.Type == "TestFlow";
});
await t.TestAsync("FlowResult creation", () => {
    var result = new FlowResult(true, 5, TimeSpan.FromSeconds(1));
    return result.IsSuccess && result.CompletedSteps == 5;
});

// ============================================================================
// 6. InMemory Backend
// ============================================================================
if (mode == "inmemory" || mode == "all" || mode == "mixed")
{
    t.Section("6. InMemory Backend");
    await TestInMemoryBackend(t);
}

// ============================================================================
// 7. Redis Backend
// ============================================================================
if (mode == "redis" || mode == "all" || mode == "mixed")
{
    t.Section("7. Redis Backend");
    await TestRedisBackend(t, redisConnection);
}

// ============================================================================
// 8. NATS Backend
// ============================================================================
if (mode == "nats" || mode == "all" || mode == "mixed")
{
    t.Section("8. NATS Backend");
    await TestNatsBackend(t, natsUrl);
}

// ============================================================================
// 9. Mixed Backend
// ============================================================================
if (mode == "mixed" || mode == "all")
{
    t.Section("9. Mixed Backend (Redis Transport + InMemory Persistence)");
    await TestMixedBackend(t, redisConnection);
}

t.PrintSummary();
return t.Failed > 0 ? 1 : 0;


// ============================================================================
// InMemory Backend Tests
// ============================================================================
async Task TestInMemoryBackend(AotTester t)
{
    ServiceProvider? sp = null;
    try
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCatga();
        services.UseMemoryPackSerializer();
        services.AddInMemoryPersistence();
        services.AddInMemoryTransport();
        services.AddCatgaResilience();
        
        sp = services.BuildServiceProvider();
        await t.TestAsync("InMemory: DI Setup", () => sp != null);
        
        var mediator = sp.GetService<ICatgaMediator>();
        await t.TestAsync("InMemory: ICatgaMediator", () => mediator != null);
        
        // Event Store
        var eventStore = sp.GetService<IEventStore>();
        await t.TestAsync("InMemory: IEventStore", () => eventStore != null);
        if (eventStore != null)
        {
            var streamId = $"test-{Guid.NewGuid()}";
            await eventStore.AppendAsync(streamId, new List<IEvent> { new TestEvent { Data = "Created" } }, -1);
            await t.TestAsync("InMemory: EventStore.AppendAsync()", () => true);
            
            var stream = await eventStore.ReadAsync(streamId);
            await t.TestAsync("InMemory: EventStore.ReadAsync()", () => stream.Events.Count == 1);
            
            await eventStore.AppendAsync(streamId, new List<IEvent> { new TestEvent { Data = "Updated" } }, 0);
            stream = await eventStore.ReadAsync(streamId);
            await t.TestAsync("InMemory: EventStore multiple events", () => stream.Events.Count == 2);
        }
        
        // Snapshot Store
        var snapshotStore = sp.GetService<ISnapshotStore>();
        await t.TestAsync("InMemory: ISnapshotStore", () => snapshotStore != null);
        if (snapshotStore != null)
        {
            var aggId = $"agg-{Guid.NewGuid()}";
            await snapshotStore.SaveAsync(aggId, new TestSnapshot { Value = 42 }, 1);
            await t.TestAsync("InMemory: SnapshotStore.SaveAsync()", () => true);
            
            var snapshot = await snapshotStore.LoadAsync<TestSnapshot>(aggId);
            await t.TestAsync("InMemory: SnapshotStore.LoadAsync()", () => snapshot?.State?.Value == 42);
        }
        
        // Idempotency Store
        var idempotencyStore = sp.GetService<IIdempotencyStore>();
        await t.TestAsync("InMemory: IIdempotencyStore", () => idempotencyStore != null);
        if (idempotencyStore != null)
        {
            var msgId = new SnowflakeIdGenerator(1).NextId();
            var exists = await idempotencyStore.HasBeenProcessedAsync(msgId);
            await t.TestAsync("InMemory: Idempotency - not exists", () => !exists);
            
            await idempotencyStore.MarkAsProcessedAsync<string>(msgId, "result");
            exists = await idempotencyStore.HasBeenProcessedAsync(msgId);
            await t.TestAsync("InMemory: Idempotency - exists after mark", () => exists);
        }
        
        // Transport
        var transport = sp.GetService<IMessageTransport>();
        await t.TestAsync("InMemory: IMessageTransport", () => transport != null);
        if (transport != null)
        {
            await transport.PublishAsync(new TestEvent { Data = "test" });
            await t.TestAsync("InMemory: Transport.PublishAsync()", () => true);
        }
        
        // Dead Letter Queue
        var dlq = sp.GetService<IDeadLetterQueue>();
        await t.TestAsync("InMemory: IDeadLetterQueue", () => dlq != null);
        
        // Distributed Lock
        var lockProvider = sp.GetService<IDistributedLockProvider>();
        await t.TestAsync("InMemory: IDistributedLockProvider", () => lockProvider != null);
        if (lockProvider != null)
        {
            var lockKey = $"lock-{Guid.NewGuid()}";
            var distLock = lockProvider.CreateLock(lockKey);
            await using var handle = await distLock.TryAcquireAsync(TimeSpan.FromSeconds(5));
            await t.TestAsync("InMemory: Lock acquire", () => handle != null);
        }
        
        // Resilience
        var resilienceProvider = sp.GetService<IResiliencePipelineProvider>();
        await t.TestAsync("InMemory: IResiliencePipelineProvider", () => resilienceProvider != null);
    }
    catch (Exception ex) { t.Fail("InMemory: Backend", ex); }
    finally { sp?.Dispose(); }
}


// ============================================================================
// Redis Backend Tests
// ============================================================================
async Task TestRedisBackend(AotTester t, string connectionString)
{
    IConnectionMultiplexer? redis = null;
    ServiceProvider? sp = null;
    try
    {
        redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await t.TestAsync("Redis: Connection", () => redis.IsConnected);
        
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCatga();
        services.UseMemoryPackSerializer();
        services.AddSingleton(redis);
        services.AddCatgaResilience();
        services.AddRedisTransport(redis);
        services.AddRedisEventStore();
        services.AddRedisSnapshotStore();
        services.AddRedisIdempotencyStore();
        services.AddRedisDeadLetterQueue();
        services.AddRedisDistributedLock();
        services.AddRedisProjectionCheckpointStore();
        
        sp = services.BuildServiceProvider();
        await t.TestAsync("Redis: DI Setup", () => sp != null);
        
        var mediator = sp.GetService<ICatgaMediator>();
        await t.TestAsync("Redis: ICatgaMediator", () => mediator != null);
        
        var eventStore = sp.GetService<IEventStore>();
        await t.TestAsync("Redis: IEventStore", () => eventStore != null);
        
        var snapshotStore = sp.GetService<ISnapshotStore>();
        await t.TestAsync("Redis: ISnapshotStore", () => snapshotStore != null);
        
        // Idempotency Store with actual operations
        var idempotencyStore = sp.GetService<IIdempotencyStore>();
        await t.TestAsync("Redis: IIdempotencyStore", () => idempotencyStore != null);
        if (idempotencyStore != null)
        {
            var msgId = new SnowflakeIdGenerator(2).NextId();
            var exists = await idempotencyStore.HasBeenProcessedAsync(msgId);
            await t.TestAsync("Redis: Idempotency - not exists", () => !exists);
            
            await idempotencyStore.MarkAsProcessedAsync<string>(msgId, "result");
            exists = await idempotencyStore.HasBeenProcessedAsync(msgId);
            await t.TestAsync("Redis: Idempotency - exists after mark", () => exists);
        }
        
        var transport = sp.GetService<IMessageTransport>();
        await t.TestAsync("Redis: IMessageTransport", () => transport != null);
        
        var dlq = sp.GetService<IDeadLetterQueue>();
        await t.TestAsync("Redis: IDeadLetterQueue", () => dlq != null);
        
        // Distributed Lock with actual operations
        var lockProvider = sp.GetService<IDistributedLockProvider>();
        await t.TestAsync("Redis: IDistributedLockProvider", () => lockProvider != null);
        if (lockProvider != null)
        {
            var lockKey = $"redis-lock-{Guid.NewGuid()}";
            var distLock = lockProvider.CreateLock(lockKey);
            await using var handle = await distLock.TryAcquireAsync(TimeSpan.FromSeconds(5));
            await t.TestAsync("Redis: Lock acquire", () => handle != null);
        }
        
        var checkpointStore = sp.GetService<IProjectionCheckpointStore>();
        await t.TestAsync("Redis: IProjectionCheckpointStore", () => checkpointStore != null);
    }
    catch (RedisConnectionException ex)
    {
        t.Skip("Redis: All tests", $"Cannot connect: {ex.Message}");
    }
    catch (Exception ex) { t.Fail("Redis: Backend", ex); }
    finally
    {
        if (sp != null) await sp.DisposeAsync();
        redis?.Dispose();
    }
}


// ============================================================================
// NATS Backend Tests
// ============================================================================
async Task TestNatsBackend(AotTester t, string url)
{
    INatsConnection? nats = null;
    ServiceProvider? sp = null;
    try
    {
        nats = new NatsConnection(new NatsOpts { Url = url });
        await nats.ConnectAsync();
        await t.TestAsync("NATS: Connection", () => nats.ConnectionState == NatsConnectionState.Open);
        
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCatga();
        services.UseMemoryPackSerializer();
        services.AddSingleton(nats);
        services.AddCatgaResilience();
        services.AddNatsTransport(url);
        
        sp = services.BuildServiceProvider();
        await t.TestAsync("NATS: DI Setup", () => sp != null);
        
        var mediator = sp.GetService<ICatgaMediator>();
        await t.TestAsync("NATS: ICatgaMediator", () => mediator != null);
        
        var transport = sp.GetService<IMessageTransport>();
        await t.TestAsync("NATS: IMessageTransport", () => transport != null);
        
        var resilienceProvider = sp.GetService<IResiliencePipelineProvider>();
        await t.TestAsync("NATS: IResiliencePipelineProvider", () => resilienceProvider != null);
    }
    catch (NatsException ex)
    {
        t.Skip("NATS: All tests", $"Cannot connect: {ex.Message}");
    }
    catch (Exception ex) { t.Fail("NATS: Backend", ex); }
    finally
    {
        if (sp != null) await sp.DisposeAsync();
        if (nats != null) await nats.DisposeAsync();
    }
}

// ============================================================================
// Mixed Backend Tests
// ============================================================================
async Task TestMixedBackend(AotTester t, string redisConnection)
{
    IConnectionMultiplexer? redis = null;
    ServiceProvider? sp = null;
    try
    {
        redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        await t.TestAsync("Mixed: Redis Connection", () => redis.IsConnected);
        
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCatga();
        services.UseMemoryPackSerializer();
        services.AddSingleton(redis);
        services.AddCatgaResilience();
        
        // Mixed: Redis Transport + InMemory Persistence
        services.AddRedisTransport(redis);
        services.AddInMemoryPersistence();
        
        sp = services.BuildServiceProvider();
        await t.TestAsync("Mixed: DI Setup", () => sp != null);
        
        var transport = sp.GetService<IMessageTransport>();
        await t.TestAsync("Mixed: Redis Transport", () => transport?.GetType().Name.Contains("Redis") == true);
        
        var eventStore = sp.GetService<IEventStore>();
        await t.TestAsync("Mixed: InMemory EventStore", () => eventStore?.GetType().Name.Contains("InMemory") == true);
        
        // Test actual operations
        if (eventStore != null)
        {
            var streamId = $"mixed-{Guid.NewGuid()}";
            await eventStore.AppendAsync(streamId, new List<IEvent> { new TestEvent { Data = "mixed" } }, -1);
            var stream = await eventStore.ReadAsync(streamId);
            await t.TestAsync("Mixed: EventStore operations", () => stream.Events.Count == 1);
        }
        
        var idempotencyStore = sp.GetService<IIdempotencyStore>();
        await t.TestAsync("Mixed: InMemory Idempotency", () => idempotencyStore?.GetType().Name.Contains("InMemory") == true);
        
        if (idempotencyStore != null)
        {
            var msgId = new SnowflakeIdGenerator(3).NextId();
            await idempotencyStore.MarkAsProcessedAsync<string>(msgId, "mixed-result");
            var exists = await idempotencyStore.HasBeenProcessedAsync(msgId);
            await t.TestAsync("Mixed: Idempotency operations", () => exists);
        }
    }
    catch (RedisConnectionException ex)
    {
        t.Skip("Mixed: All tests", $"Cannot connect to Redis: {ex.Message}");
    }
    catch (Exception ex) { t.Fail("Mixed: Backend", ex); }
    finally
    {
        if (sp != null) await sp.DisposeAsync();
        redis?.Dispose();
    }
}


// ============================================================================
// Test Helper
// ============================================================================
public class AotTester
{
    public int Passed { get; private set; }
    public int Failed { get; private set; }
    public int Skipped { get; private set; }

    public void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine($"┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine($"│ {name,-59} │");
        Console.WriteLine($"└─────────────────────────────────────────────────────────────┘");
    }

    public async Task TestAsync(string name, Func<bool> test)
    {
        try { if (test()) Pass(name); else Fail(name); }
        catch (Exception ex) { Fail(name, ex); }
        await Task.CompletedTask;
    }

    public void Pass(string test)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ {test}");
        Console.ResetColor();
        Passed++;
    }

    public void Fail(string test, Exception? ex = null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ {test}");
        if (ex != null) Console.WriteLine($"    Error: {ex.Message}");
        Console.ResetColor();
        Failed++;
    }

    public void Skip(string test, string reason)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ⊘ {test} (skipped: {reason})");
        Console.ResetColor();
        Skipped++;
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                         Summary                              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        if (Failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  All {Passed} tests passed! ({Skipped} skipped)");
            Console.WriteLine("  AOT validation successful.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  {Failed} test(s) failed, {Passed} passed, {Skipped} skipped.");
        }
        Console.ResetColor();
        Console.WriteLine();
    }
}

// ============================================================================
// Test Types
// ============================================================================
public record TestQuery : QueryBase<string> { public string Name { get; init; } = ""; }
public record TestCommand : CommandBase { public string Name { get; init; } = ""; }
public record TestEvent : EventBase { public string Data { get; init; } = ""; }
public record DelayedTestEvent : EventBase, IDelayedMessage
{
    public string Data { get; init; } = "";
    public DateTimeOffset DeliverAt { get; init; }
}
public record PrioritizedTestEvent : EventBase, IPrioritizedMessage
{
    public string Data { get; init; } = "";
    public MessagePriority Priority { get; init; }
}

[MemoryPackable]
public partial class SerializableMessage { public int Id { get; set; } public string Name { get; set; } = ""; }

[MemoryPackable]
public partial class ComplexMessage
{
    public List<string>? Items { get; set; }
    public Dictionary<string, int>? Metadata { get; set; }
}

[MemoryPackable]
public partial class TestSnapshot { public int Value { get; set; } }

public class TestJsonObject { public int Id { get; set; } public string Name { get; set; } = ""; }

[JsonSerializable(typeof(TestJsonObject))]
public partial class AotJsonContext : JsonSerializerContext { }
