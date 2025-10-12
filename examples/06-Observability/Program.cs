using Catga;
using Catga.Messages;
using Catga.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Setup OpenTelemetry for distributed tracing and metrics
var services = new ServiceCollection();

services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

// Add OpenTelemetry Tracing (ActivitySource)
services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Catga.ObservabilityDemo"))
    .WithTracing(tracing => tracing
        .AddSource(CatgaDiagnostics.ActivitySourceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(CatgaDiagnostics.MeterName)
        .AddConsoleExporter());

// Add Catga
services.AddCatga();

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<ICatgaMediator>();

Console.WriteLine("🔍 Catga Observability Demo\n");
Console.WriteLine("📊 Metrics & Traces will be exported to console\n");

// Send commands and events
for (int i = 1; i <= 5; i++)
{
    Console.WriteLine($"--- Iteration {i} ---");

    var result = await mediator.SendAsync<TestCommand, string>(new TestCommand { Value = $"Test-{i}" });
    Console.WriteLine($"Command result: {(result.IsSuccess ? "✅" : "❌")} {result.Value}\n");

    await mediator.PublishAsync(new TestEvent { Message = $"Event-{i}" });
    Console.WriteLine($"Event published\n");

    await Task.Delay(100);
}

Console.WriteLine("\n📈 Observability Summary:");
Console.WriteLine("  • ActivitySource: Catga");
Console.WriteLine("  • Meter: Catga");
Console.WriteLine("  • Traces: Command.Execute, Event.Publish, Message.Publish");
Console.WriteLine("  • Metrics: catga.commands.executed, catga.events.published, catga.message.duration");
Console.WriteLine("\n✅ Demo completed!");

// Test types
public record TestCommand : IRequest<string>
{
    public string Value { get; init; } = "";
    public string? MessageId => Guid.NewGuid().ToString();
    public string? CorrelationId => null;
}

public class TestCommandHandler : IRequestHandler<TestCommand, string>
{
    public ValueTask<string> HandleAsync(TestCommand request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult($"Processed: {request.Value}");
}

public record TestEvent : IEvent
{
    public string Message { get; init; } = "";
    public string? MessageId => Guid.NewGuid().ToString();
    public string? CorrelationId => null;
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    public ValueTask HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  📨 Event handled: {@event.Message}");
        return ValueTask.CompletedTask;
    }
}

