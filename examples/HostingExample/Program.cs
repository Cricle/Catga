// Catga Hosting Example - Worker Service with Hosted Services
// Demonstrates: RecoveryHostedService, TransportHostedService, OutboxProcessorService, Health Checks

using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Hosting;
using HostingExample;
using MemoryPack;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = Host.CreateApplicationBuilder(args);

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║         Catga Hosting Example - Worker Service              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Configure Catga with hosted services
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices(options =>
    {
        // Recovery service configuration
        options.Recovery.CheckInterval = TimeSpan.FromSeconds(15);
        options.Recovery.MaxRetries = 3;
        options.Recovery.RetryDelay = TimeSpan.FromSeconds(5);
        
        // Outbox processor configuration
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(3);
        options.OutboxProcessor.BatchSize = 50;
        
        // Shutdown timeout
        options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        
        Console.WriteLine("✓ Hosted Services Configured:");
        Console.WriteLine($"  - Recovery Check Interval: {options.Recovery.CheckInterval.TotalSeconds}s");
        Console.WriteLine($"  - Outbox Scan Interval: {options.OutboxProcessor.ScanInterval.TotalSeconds}s");
        Console.WriteLine($"  - Shutdown Timeout: {options.ShutdownTimeout.TotalSeconds}s");
    });

// Add transport
builder.Services.AddInMemoryTransport();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
Console.WriteLine("✓ Health Checks Enabled");

// Add background worker
builder.Services.AddHostedService<MessageProducerWorker>();
Console.WriteLine("✓ Message Producer Worker Registered");

Console.WriteLine();

var host = builder.Build();

// Display startup information
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                    Service Started                           ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine("║ Hosted Services:                                             ║");
Console.WriteLine("║   ✓ RecoveryHostedService   - Health monitoring              ║");
Console.WriteLine("║   ✓ TransportHostedService  - Lifecycle management           ║");
Console.WriteLine("║   ✓ OutboxProcessorService  - Background processing          ║");
Console.WriteLine("║   ✓ MessageProducerWorker   - Demo message producer          ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine("║ Press Ctrl+C to test graceful shutdown                       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

await host.RunAsync();

namespace HostingExample
{
    // Demo messages
    [MemoryPackable]
    public partial record ProcessDataCommand(string Data, DateTime Timestamp) : IRequest
    {
        public long MessageId { get; init; }
    }

    [MemoryPackable]
    public partial record DataProcessedEvent(string Data, DateTime ProcessedAt) : IEvent
    {
        public long MessageId { get; init; }
    }

    // Command handler
    public class ProcessDataHandler : IRequestHandler<ProcessDataCommand>
    {
        private readonly ILogger<ProcessDataHandler> _logger;
        private readonly ICatgaMediator _mediator;

        public ProcessDataHandler(ILogger<ProcessDataHandler> logger, ICatgaMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        public async ValueTask<CatgaResult> HandleAsync(ProcessDataCommand request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Processing data: {Data}", request.Data);
            
            // Simulate processing
            await Task.Delay(100, cancellationToken);
            
            // Publish event
            await _mediator.PublishAsync(new DataProcessedEvent(request.Data, DateTime.UtcNow), cancellationToken);
            
            return CatgaResult.Success();
        }
    }

    // Event handler
    public class DataProcessedEventHandler : IEventHandler<DataProcessedEvent>
    {
        private readonly ILogger<DataProcessedEventHandler> _logger;

        public DataProcessedEventHandler(ILogger<DataProcessedEventHandler> logger)
        {
            _logger = logger;
        }

        public ValueTask HandleAsync(DataProcessedEvent @event, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("✓ Data processed: {Data} at {Time}", @event.Data, @event.ProcessedAt);
            return ValueTask.CompletedTask;
        }
    }

    // Background worker that produces messages
    public class MessageProducerWorker : BackgroundService
    {
        private readonly ILogger<MessageProducerWorker> _logger;
        private readonly ICatgaMediator _mediator;
        private int _messageCount = 0;

        public MessageProducerWorker(ILogger<MessageProducerWorker> logger, ICatgaMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Message Producer Worker started");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    _messageCount++;
                    var command = new ProcessDataCommand($"Message-{_messageCount}", DateTime.UtcNow);
                    
                    var result = await _mediator.SendAsync(command, stoppingToken);
                    
                    if (result.IsSuccess)
                    {
                        _logger.LogInformation("→ Sent message #{Count}", _messageCount);
                    }
                    else
                    {
                        _logger.LogError("Failed to send message: {Error}", result.Error);
                    }

                    // Wait 5 seconds between messages
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Message Producer Worker is stopping (graceful shutdown)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message Producer Worker encountered an error");
            }

            _logger.LogInformation("Message Producer Worker stopped. Total messages sent: {Count}", _messageCount);
        }
    }
}
