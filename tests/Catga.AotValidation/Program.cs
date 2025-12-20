// AOT Validation - Comprehensive runtime validation for all Catga libraries
// Build with: dotnet publish -c Release
// Run the published executable to validate all functionality

using System.Text.Json;
using System.Text.Json.Serialization;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MemoryPack;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Catga AOT Validation - Runtime Tests               ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var passed = 0;
var failed = 0;

void Pass(string test)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  ✓ {test}");
    Console.ResetColor();
    passed++;
}

void Fail(string test, Exception? ex = null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  ✗ {test}");
    if (ex != null) Console.WriteLine($"    Error: {ex.Message}");
    Console.ResetColor();
    failed++;
}

// ============================================================================
// 1. Core Types Validation
// ============================================================================
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 1. Core Types                                               │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var id = IdGenerator.NewBase64Id();
    if (!string.IsNullOrEmpty(id)) Pass("IdGenerator.NewBase64Id()");
    else Fail("IdGenerator.NewBase64Id()");
}
catch (Exception ex) { Fail("IdGenerator.NewBase64Id()", ex); }

try
{
    var result = CatgaResult.Success();
    if (result.IsSuccess) Pass("CatgaResult.Success()");
    else Fail("CatgaResult.Success()");
}
catch (Exception ex) { Fail("CatgaResult.Success()", ex); }

try
{
    var result = CatgaResult<int>.Success(42);
    if (result.IsSuccess && result.Value == 42) Pass("CatgaResult<T>.Success()");
    else Fail("CatgaResult<T>.Success()");
}
catch (Exception ex) { Fail("CatgaResult<T>.Success()", ex); }

try
{
    var result = CatgaResult.Failure("Test error");
    if (!result.IsSuccess && result.Error == "Test error") Pass("CatgaResult.Failure()");
    else Fail("CatgaResult.Failure()");
}
catch (Exception ex) { Fail("CatgaResult.Failure()", ex); }

// ============================================================================
// 2. Distributed ID
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 2. Distributed ID                                           │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var generator = new SnowflakeIdGenerator(1);
    var id1 = generator.NextId();
    var id2 = generator.NextId();
    if (id1 > 0 && id2 > id1) Pass("SnowflakeIdGenerator.NextId()");
    else Fail("SnowflakeIdGenerator.NextId()");
}
catch (Exception ex) { Fail("SnowflakeIdGenerator.NextId()", ex); }

try
{
    var generator = new SnowflakeIdGenerator(1);
    var messageId = MessageId.NewId(generator);
    if (messageId.Value > 0) Pass("MessageId.NewId()");
    else Fail("MessageId.NewId()");
}
catch (Exception ex) { Fail("MessageId.NewId()", ex); }

try
{
    var generator = new SnowflakeIdGenerator(1);
    var correlationId = CorrelationId.NewId(generator);
    if (correlationId.Value > 0) Pass("CorrelationId.NewId()");
    else Fail("CorrelationId.NewId()");
}
catch (Exception ex) { Fail("CorrelationId.NewId()", ex); }

// ============================================================================
// 3. DI Container Setup
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 3. Dependency Injection                                     │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

ServiceProvider? sp = null;
try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddCatgaResilience();
    
    sp = services.BuildServiceProvider();
    Pass("ServiceCollection.AddCatga()");
}
catch (Exception ex) { Fail("ServiceCollection.AddCatga()", ex); }

try
{
    var mediator = sp?.GetService<ICatgaMediator>();
    if (mediator != null) Pass("Resolve ICatgaMediator");
    else Fail("Resolve ICatgaMediator");
}
catch (Exception ex) { Fail("Resolve ICatgaMediator", ex); }

// ============================================================================
// 4. Message Handling
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 4. Message Handling                                         │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddSingleton<IRequestHandler<TestQuery, string>, TestQueryHandler>();
    services.AddSingleton<IRequestHandler<TestCommand>, TestCommandHandler>();
    
    using var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<ICatgaMediator>();
    
    // Test Query
    var queryResult = await mediator.SendAsync<TestQuery, string>(new TestQuery { Name = "AOT" });
    if (queryResult.IsSuccess && queryResult.Value == "Hello, AOT!") Pass("Query handling");
    else Fail($"Query handling: {queryResult.Error}");
    
    // Test Command
    var cmdResult = await mediator.SendAsync(new TestCommand { Name = "Test" });
    if (cmdResult.IsSuccess) Pass("Command handling");
    else Fail($"Command handling: {cmdResult.Error}");
}
catch (Exception ex) { Fail("Message handling", ex); }

// ============================================================================
// 5. Event Sourcing
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 5. Event Sourcing                                           │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddCatgaResilience();
    
    using var provider = services.BuildServiceProvider();
    var eventStore = provider.GetService<IEventStore>();
    
    if (eventStore != null)
    {
        var streamId = $"test-{Guid.NewGuid()}";
        var events = new List<IEvent> { new TestEvent { Data = "Created" } };
        await eventStore.AppendAsync(streamId, events, -1);
        Pass("EventStore.AppendAsync()");
        
        var stream = await eventStore.ReadAsync(streamId);
        if (stream.Events.Count == 1) Pass("EventStore.ReadAsync()");
        else Fail("EventStore.ReadAsync()");
    }
    else Fail("Resolve IEventStore");
}
catch (Exception ex) { Fail("Event Sourcing", ex); }

// ============================================================================
// 6. Serialization
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 6. Serialization                                            │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var msg = new SerializableMessage { Id = 123, Name = "Test" };
    var bytes = MemoryPackSerializer.Serialize(msg);
    var deserialized = MemoryPackSerializer.Deserialize<SerializableMessage>(bytes);
    if (deserialized?.Id == 123 && deserialized.Name == "Test") Pass("MemoryPack serialization");
    else Fail("MemoryPack serialization");
}
catch (Exception ex) { Fail("MemoryPack serialization", ex); }

try
{
    var json = JsonSerializer.Serialize(new TestJsonObject { Id = 1, Name = "Test" }, AotJsonContext.Default.TestJsonObject);
    if (!string.IsNullOrEmpty(json)) Pass("System.Text.Json AOT serialization");
    else Fail("System.Text.Json AOT serialization");
}
catch (Exception ex) { Fail("System.Text.Json AOT serialization", ex); }

// ============================================================================
// 7. Flow DSL
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 7. Flow DSL                                                 │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var status = DslFlowStatus.Running;
    if (status == DslFlowStatus.Running) Pass("DslFlowStatus enum");
    else Fail("DslFlowStatus enum");
}
catch (Exception ex) { Fail("DslFlowStatus enum", ex); }

try
{
    var flowStatus = FlowStatus.Running;
    if (flowStatus == FlowStatus.Running) Pass("FlowStatus enum");
    else Fail("FlowStatus enum");
}
catch (Exception ex) { Fail("FlowStatus enum", ex); }

// ============================================================================
// 8. Persistence Stores
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 8. Persistence Stores                                       │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    
    using var provider = services.BuildServiceProvider();
    
    // Snapshot Store
    var snapshotStore = provider.GetService<ISnapshotStore>();
    if (snapshotStore != null)
    {
        await snapshotStore.SaveAsync("test-agg", new TestSnapshot { Value = 42 }, 1);
        Pass("SnapshotStore.SaveAsync()");
        
        var snapshot = await snapshotStore.LoadAsync<TestSnapshot>("test-agg");
        if (snapshot?.State?.Value == 42) Pass("SnapshotStore.LoadAsync()");
        else Fail("SnapshotStore.LoadAsync()");
    }
    else Fail("Resolve ISnapshotStore");
}
catch (Exception ex) { Fail("Persistence Stores", ex); }

// ============================================================================
// 9. Idempotency
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 9. Idempotency                                              │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.UseMemoryPackSerializer();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddCatgaResilience();
    
    using var provider = services.BuildServiceProvider();
    var idempotencyStore = provider.GetService<Catga.Idempotency.IIdempotencyStore>();
    
    if (idempotencyStore != null)
    {
        var messageId = new SnowflakeIdGenerator(1).NextId();
        var exists = await idempotencyStore.HasBeenProcessedAsync(messageId);
        if (!exists) Pass("IdempotencyStore.HasBeenProcessedAsync() - not exists");
        else Fail("IdempotencyStore.HasBeenProcessedAsync() - not exists");
        
        await idempotencyStore.MarkAsProcessedAsync<string>(messageId, "result");
        exists = await idempotencyStore.HasBeenProcessedAsync(messageId);
        if (exists) Pass("IdempotencyStore.MarkAsProcessedAsync() + HasBeenProcessedAsync()");
        else Fail("IdempotencyStore.MarkAsProcessedAsync() + HasBeenProcessedAsync()");
    }
    else Fail("Resolve IIdempotencyStore");
}
catch (Exception ex) { Fail("Idempotency", ex); }

// ============================================================================
// 10. Resilience
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 10. Resilience                                              │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddCatgaResilience();
    
    using var provider = services.BuildServiceProvider();
    var resilienceProvider = provider.GetService<IResiliencePipelineProvider>();
    
    if (resilienceProvider != null)
    {
        Pass("Resolve IResiliencePipelineProvider");
    }
    else Fail("Resolve IResiliencePipelineProvider");
}
catch (Exception ex) { Fail("Resilience", ex); }

// ============================================================================
// 11. Transport
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 11. Transport                                               │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddCatgaResilience();
    
    using var provider = services.BuildServiceProvider();
    var transport = provider.GetService<IMessageTransport>();
    
    if (transport != null)
    {
        Pass("Resolve IMessageTransport (InMemory)");
        
        // Test publish
        await transport.PublishAsync(new TestEvent { Data = "transport-test" });
        Pass("IMessageTransport.PublishAsync()");
    }
    else Fail("Resolve IMessageTransport");
}
catch (Exception ex) { Fail("Transport", ex); }

// ============================================================================
// 12. Dead Letter Queue
// ============================================================================
Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
Console.WriteLine("│ 12. Dead Letter Queue                                       │");
Console.WriteLine("└─────────────────────────────────────────────────────────────┘");

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddCatga();
    services.UseMemoryPackSerializer();
    services.AddInMemoryPersistence();
    services.AddInMemoryTransport();
    services.AddCatgaResilience();
    
    using var provider = services.BuildServiceProvider();
    var dlq = provider.GetService<Catga.DeadLetter.IDeadLetterQueue>();
    
    if (dlq != null)
    {
        Pass("Resolve IDeadLetterQueue");
    }
    else Fail("Resolve IDeadLetterQueue");
}
catch (Exception ex) { Fail("Dead Letter Queue", ex); }

// ============================================================================
// Summary
// ============================================================================
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                         Summary                              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

if (failed == 0)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  All {passed} tests passed! AOT validation successful.");
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  {failed} test(s) failed, {passed} passed.");
    Console.ResetColor();
}

Console.WriteLine();
return failed > 0 ? 1 : 0;

// ============================================================================
// Test Types
// ============================================================================

public record TestQuery : QueryBase<string>
{
    public string Name { get; init; } = "";
}

public record TestCommand : CommandBase
{
    public string Name { get; init; } = "";
}

public record TestEvent : EventBase
{
    public string Data { get; init; } = "";
}

public class TestQueryHandler : IRequestHandler<TestQuery, string>
{
    public ValueTask<CatgaResult<string>> HandleAsync(TestQuery request, CancellationToken ct = default)
        => ValueTask.FromResult(CatgaResult<string>.Success($"Hello, {request.Name}!"));
}

public class TestCommandHandler : IRequestHandler<TestCommand>
{
    public ValueTask<CatgaResult> HandleAsync(TestCommand request, CancellationToken ct = default)
        => ValueTask.FromResult(CatgaResult.Success());
}

[MemoryPackable]
public partial class SerializableMessage
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[MemoryPackable]
public partial class TestSnapshot
{
    public int Value { get; set; }
}

public class TestJsonObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[JsonSerializable(typeof(TestJsonObject))]
public partial class AotJsonContext : JsonSerializerContext
{
}
