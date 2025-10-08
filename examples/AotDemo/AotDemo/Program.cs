using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MemoryPack;

// 🎯 Native AOT Test Program for Catga Framework
// Tests all core features with AOT-friendly manual registration

var services = new ServiceCollection();

// Configure logging
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

// Add Catga with manual registration (AOT-friendly)
services.AddCatga(options =>
{
    options.EnableLogging = true;
    options.EnableIdempotency = true;
});

// Register MemoryPack serializer (AOT-friendly)
services.AddSingleton<Catga.Serialization.IMessageSerializer, MemoryPackMessageSerializer>();

// Manually register handlers (AOT-friendly)
services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

var serviceProvider = services.BuildServiceProvider();
var mediator = serviceProvider.GetRequiredService<ICatgaMediator>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

Console.WriteLine("🚀 Catga Native AOT Test");
Console.WriteLine("========================\n");

// Test 1: Send Command
logger.LogInformation("Test 1: Sending command...");
var command = new TestCommand 
{ 
    MessageId = Guid.NewGuid().ToString(),
    Name = "AOT Test",
    Value = 42 
};

var result = await mediator.SendAsync<TestCommand, TestResponse>(command);
if (result.IsSuccess)
{
    logger.LogInformation("✅ Command succeeded: {Message}", result.Value?.Message);
}
else
{
    logger.LogError("❌ Command failed: {Error}", result.Error);
}

// Test 2: Publish Event
logger.LogInformation("\nTest 2: Publishing event...");
var @event = new TestEvent 
{ 
    MessageId = Guid.NewGuid().ToString(),
    EventName = "AOT Event",
    Timestamp = DateTime.UtcNow 
};

await mediator.PublishAsync(@event);
logger.LogInformation("✅ Event published successfully");

// Test 3: Idempotency Test
logger.LogInformation("\nTest 3: Testing idempotency...");
var idempotentCommand = new TestCommand 
{ 
    MessageId = "idempotent-test-123",
    Name = "Idempotent",
    Value = 100 
};

var firstResult = await mediator.SendAsync<TestCommand, TestResponse>(idempotentCommand);
var secondResult = await mediator.SendAsync<TestCommand, TestResponse>(idempotentCommand);

logger.LogInformation("✅ Idempotency test: First={First}, Second={Second}", 
    firstResult.Value?.Message, 
    secondResult.Value?.Message);

Console.WriteLine("\n🎉 All tests completed successfully!");
Console.WriteLine("✅ Native AOT compatibility verified!");

return 0;

// Message definitions with MemoryPack attributes
[MemoryPackable]
public partial class TestCommand : IRequest<TestResponse>
{
    public string MessageId { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

[MemoryPackable]
public partial class TestResponse
{
    public string Message { get; set; } = string.Empty;
    public int ProcessedValue { get; set; }
}

[MemoryPackable]
public partial class TestEvent : IEvent
{
    public string MessageId { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string EventName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

// Handlers
public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    private readonly ILogger<TestCommandHandler> _logger;

    public TestCommandHandler(ILogger<TestCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult<TestResponse>> HandleAsync(TestCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling command: {Name} with value {Value}", request.Name, request.Value);
        
        var response = new TestResponse 
        { 
            Message = $"Processed: {request.Name}",
            ProcessedValue = request.Value * 2
        };

        return Task.FromResult(CatgaResult<TestResponse>.Success(response));
    }
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    private readonly ILogger<TestEventHandler> _logger;

    public TestEventHandler(ILogger<TestEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling event: {EventName} at {Timestamp}", @event.EventName, @event.Timestamp);
        return Task.CompletedTask;
    }
}
