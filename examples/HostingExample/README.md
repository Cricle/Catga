# Catga Hosting Example - Worker Service

A complete example demonstrating Catga's integration with Microsoft.Extensions.Hosting in a Worker Service application.

## Features Demonstrated

✅ **RecoveryHostedService** - Automatic health monitoring and component recovery  
✅ **TransportHostedService** - Message transport lifecycle management  
✅ **OutboxProcessorService** - Background message processing  
✅ **Health Checks** - Catga health check integration  
✅ **Graceful Shutdown** - Proper message completion before shutdown  
✅ **Background Workers** - Custom BackgroundService integration  
✅ **CQRS Pattern** - Commands and Events  

## Quick Start

### Run the Example

```bash
cd examples/HostingExample
dotnet run
```

### Expected Output

```
╔══════════════════════════════════════════════════════════════╗
║         Catga Hosting Example - Worker Service              ║
╚══════════════════════════════════════════════════════════════╝

✓ Hosted Services Configured:
  - Recovery Check Interval: 15s
  - Outbox Scan Interval: 3s
  - Shutdown Timeout: 30s
✓ Health Checks Enabled
✓ Message Producer Worker Registered

╔══════════════════════════════════════════════════════════════╗
║                    Service Started                           ║
╠══════════════════════════════════════════════════════════════╣
║ Hosted Services:                                             ║
║   ✓ RecoveryHostedService   - Health monitoring              ║
║   ✓ TransportHostedService  - Lifecycle management           ║
║   ✓ OutboxProcessorService  - Background processing          ║
║   ✓ MessageProducerWorker   - Demo message producer          ║
╠══════════════════════════════════════════════════════════════╣
║ Press Ctrl+C to test graceful shutdown                       ║
╚══════════════════════════════════════════════════════════════╝

info: HostingExample.MessageProducerWorker[0]
      Message Producer Worker started
info: HostingExample.MessageProducerWorker[0]
      → Sent message #1
info: HostingExample.ProcessDataHandler[0]
      Processing data: Message-1
info: HostingExample.DataProcessedEventHandler[0]
      ✓ Data processed: Message-1 at 12/23/2024 10:30:15 AM
```

### Test Graceful Shutdown

Press `Ctrl+C` to trigger graceful shutdown. You'll see:

```
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
info: HostingExample.MessageProducerWorker[0]
      Message Producer Worker is stopping (graceful shutdown)
info: HostingExample.MessageProducerWorker[0]
      Message Producer Worker stopped. Total messages sent: 5
info: Catga.Hosting.TransportHostedService[0]
      Stopping transport service
info: Catga.Hosting.TransportHostedService[0]
      Transport service stopped
```

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│              IHost (Worker Service)                     │
└────────────────────┬────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────┐
│              Hosted Services                             │
│  ┌─────────────────────────────────────────────────┐   │
│  │ RecoveryHostedService                           │   │
│  │ - Checks health every 15s                       │   │
│  │ - Auto-recovers failed components               │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ TransportHostedService                          │   │
│  │ - Manages transport lifecycle                   │   │
│  │ - Handles graceful shutdown                     │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ OutboxProcessorService                          │   │
│  │ - Processes outbox every 3s                     │   │
│  │ - Ensures reliable delivery                     │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ MessageProducerWorker (Demo)                    │   │
│  │ - Sends message every 5s                        │   │
│  │ - Demonstrates graceful shutdown                │   │
│  └─────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────┐
│                 Catga Mediator                          │
│  (Command/Event Routing)                                │
└────────┬───────────────────────────┬────────────────────┘
         │                           │
┌────────▼────────┐         ┌────────▼────────────────────┐
│   Handlers      │         │   Event Handlers            │
│  - ProcessData  │         │  - DataProcessed            │
└─────────────────┘         └─────────────────────────────┘
```

## Configuration

### Hosted Services Configuration

```csharp
.AddHostedServices(options =>
{
    // Recovery service - monitors component health
    options.Recovery.CheckInterval = TimeSpan.FromSeconds(15);
    options.Recovery.MaxRetries = 3;
    options.Recovery.RetryDelay = TimeSpan.FromSeconds(5);
    
    // Outbox processor - background message processing
    options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(3);
    options.OutboxProcessor.BatchSize = 50;
    
    // Graceful shutdown timeout
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

### Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

Health checks include:
- **catga_transport**: Transport layer connection status
- **catga_persistence**: Persistence layer availability
- **catga_recovery**: Recovery service and component health

## Key Concepts

### 1. Hosted Services Lifecycle

All Catga hosted services integrate with `IHostApplicationLifetime`:

- **Startup**: Services start automatically when the host starts
- **Running**: Services run in the background
- **Shutdown**: Services stop gracefully when Ctrl+C is pressed or SIGTERM is received

### 2. Graceful Shutdown

When you press Ctrl+C:

1. `ApplicationStopping` event is triggered
2. Transport stops accepting new messages
3. Current messages are allowed to complete
4. All hosted services stop gracefully
5. Application exits

### 3. Background Workers

Custom background workers (like `MessageProducerWorker`) can coexist with Catga's hosted services:

```csharp
public class MessageProducerWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Send messages
            await _mediator.SendAsync(command, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

### 4. Health Monitoring

RecoveryHostedService automatically monitors all registered `IRecoverableComponent` instances and attempts recovery when they become unhealthy.

## Testing Scenarios

### Scenario 1: Normal Operation

Run the application and observe:
- Messages being sent every 5 seconds
- Events being processed
- Hosted services running in the background

### Scenario 2: Graceful Shutdown

Press Ctrl+C and observe:
- Message producer stops immediately
- Current messages complete processing
- All services shut down gracefully
- Total message count is logged

### Scenario 3: Long-Running Messages

Modify the handler to simulate long processing:

```csharp
public async ValueTask<CatgaResult> HandleAsync(ProcessDataCommand request, CancellationToken ct)
{
    await Task.Delay(TimeSpan.FromSeconds(20), ct);  // Simulate long processing
    // ...
}
```

Press Ctrl+C during processing and observe that the application waits for the message to complete (up to ShutdownTimeout).

## Deployment

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY bin/Release/net9.0/publish/ .
ENTRYPOINT ["dotnet", "HostingExample.dll"]
```

Build and run:
```bash
docker build -t catga-hosting-example .
docker run --rm catga-hosting-example
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: catga-hosting-example
spec:
  replicas: 1
  selector:
    matchLabels:
      app: catga-hosting-example
  template:
    metadata:
      labels:
        app: catga-hosting-example
    spec:
      containers:
      - name: worker
        image: catga-hosting-example:latest
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "200m"
```

## Related Resources

- [Hosting Configuration Guide](../../docs/guides/hosting-configuration.md) - Detailed configuration options
- [Hosting Migration Guide](../../docs/guides/hosting-migration.md) - Migrating from old APIs
- [OrderSystem Example](../OrderSystem/README.md) - Web API with hosted services
- [Getting Started](../../docs/articles/getting-started.md) - Catga basics

## Summary

This example demonstrates:

✅ Complete hosted services integration  
✅ Automatic lifecycle management  
✅ Graceful shutdown handling  
✅ Health check integration  
✅ Background worker patterns  
✅ Production-ready configuration  

Perfect starting point for building Worker Services with Catga!
