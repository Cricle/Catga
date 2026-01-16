# Catga.Cluster

Distributed cluster support for Catga using DotNext Raft consensus.

## Features

- **IClusterCoordinator** - Simple API for leader election and coordination
- **SingletonTaskRunner** - Run background tasks only on leader node
- **LeaderOnlyBehavior** - Pipeline behavior to reject non-leader requests
- **ForwardToLeaderBehavior** - Pipeline behavior to forward requests to leader
- **HttpClusterForwarder** - HTTP-based request forwarding implementation
- **DistributedLock** - Leader-only distributed locking
- **ClusterHealthCheck** - Health check for cluster status
- **RaftClusterConfiguration** - Helper for configuring Raft from IConfiguration

## Quick Start

### 1. Install Package

```bash
dotnet add package Catga.Cluster
```

### 2. Configure Raft Cluster

Add to `appsettings.json`:

```json
{
  "Cluster": {
    "LocalNodeEndpoint": "http://localhost:5000",
    "Members": ["http://localhost:5001", "http://localhost:5002"],
    "ElectionTimeout": "00:00:00.150",
    "HeartbeatInterval": "00:00:00.050",
    "PersistentStatePath": "./raft-state"
  }
}
```

### 3. Register Services

```csharp
using Catga.Cluster;
using Catga.Cluster.DependencyInjection;
using DotNext.Net.Cluster.Consensus.Raft;

var builder = WebApplication.CreateBuilder(args);

// Load cluster configuration
var clusterConfig = RaftClusterConfiguration.FromConfiguration(builder.Configuration);

// Configure DotNext Raft (using in-memory or persistent storage)
builder.Services.UseInMemoryConfigurationStorage()
    .JoinCluster(clusterConfig.LocalNodeEndpoint, clusterConfig.Members);

// Add Catga cluster support
builder.Services.AddCatgaCluster(options =>
{
    options.EnableHttpForwarder = true;
    options.ForwardTimeout = TimeSpan.FromSeconds(30);
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddClusterHealthCheck();
```

### 4. Use Leader-Only Commands

**Option A: Fail if not leader**

```csharp
// Commands will fail with error if not executed on leader
builder.Services.AddLeaderOnlyBehavior<CreateOrderCommand, OrderResult>();
```

**Option B: Forward to leader**

```csharp
// Commands will be automatically forwarded to leader via HTTP
builder.Services.AddForwardToLeaderBehavior<CreateOrderCommand, OrderResult>();
```

### 5. Singleton Background Tasks

Create a task that only runs on the leader:

```csharp
public class OutboxProcessor : SingletonTaskRunner
{
    private readonly IOutboxStore _store;

    public OutboxProcessor(
        IClusterCoordinator coordinator,
        IOutboxStore store,
        ILogger<OutboxProcessor> logger)
        : base(coordinator, logger, checkInterval: TimeSpan.FromSeconds(1))
    {
        _store = store;
    }

    protected override string TaskName => "OutboxProcessor";

    protected override async Task ExecuteLeaderTaskAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Process outbox - only runs on leader
            var messages = await _store.GetPendingAsync(100, ct);
            foreach (var msg in messages)
            {
                await ProcessMessageAsync(msg, ct);
            }
            
            await Task.Delay(1000, ct);
        }
    }
}

// Register
builder.Services.AddSingletonTask<OutboxProcessor>();
```

### 6. Check Leadership in Code

```csharp
public class OrderService
{
    private readonly IClusterCoordinator _coordinator;

    public async Task<bool> ProcessOrderAsync(Order order)
    {
        // Check if this node is leader
        if (!_coordinator.IsLeader)
        {
            return false; // Or forward to leader
        }

        // Execute leader-only logic
        var (isLeader, result) = await _coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            return await SaveOrderAsync(order, ct);
        });

        return isLeader && result;
    }
}
```

## Running a Multi-Node Cluster

### Local Testing (3 nodes)

**Terminal 1 (Node 0):**
```bash
dotnet run --project examples/OrderSystem -- --cluster --port 5000 --node-id node0
```

**Terminal 2 (Node 1):**
```bash
dotnet run --project examples/OrderSystem -- --cluster --port 5001 --node-id node1
```

**Terminal 3 (Node 2):**
```bash
dotnet run --project examples/OrderSystem -- --cluster --port 5002 --node-id node2
```

### Docker Compose

```yaml
version: '3.8'
services:
  node0:
    build: .
    ports:
      - "5000:5000"
    environment:
      - Cluster__LocalNodeEndpoint=http://node0:5000
      - Cluster__Members__0=http://node1:5000
      - Cluster__Members__1=http://node2:5000
    networks:
      - catga-cluster

  node1:
    build: .
    ports:
      - "5001:5000"
    environment:
      - Cluster__LocalNodeEndpoint=http://node1:5000
      - Cluster__Members__0=http://node0:5000
      - Cluster__Members__1=http://node2:5000
    networks:
      - catga-cluster

  node2:
    build: .
    ports:
      - "5002:5000"
    environment:
      - Cluster__LocalNodeEndpoint=http://node2:5000
      - Cluster__Members__0=http://node0:5000
      - Cluster__Members__1=http://node1:5000
    networks:
      - catga-cluster

networks:
  catga-cluster:
```

## Advanced Usage

### Custom Forwarder

Implement `IClusterForwarder` for custom forwarding logic (e.g., gRPC):

```csharp
public class GrpcClusterForwarder : IClusterForwarder
{
    public async Task<CatgaResult<TResponse>> ForwardAsync<TRequest, TResponse>(
        TRequest request,
        string leaderEndpoint,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        // Custom gRPC forwarding logic
    }
}

// Register
builder.Services.AddSingleton<IClusterForwarder, GrpcClusterForwarder>();
```

### Distributed Locking

```csharp
public class CriticalService
{
    private readonly IClusterCoordinator _coordinator;

    public async Task<bool> ExecuteCriticalOperationAsync()
    {
        await using var lockHandle = await DistributedLock.TryAcquireAsync(
            _coordinator,
            "critical-operation",
            timeout: TimeSpan.FromSeconds(5));

        if (lockHandle == null || !lockHandle.IsValid)
        {
            return false; // Could not acquire lock
        }

        // Execute critical operation with lock held
        await DoWorkAsync();
        return true;
    }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Catga.Cluster                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────────┐      ┌──────────────────┐           │
│  │ ClusterCoordinator│◄────│  IRaftCluster    │           │
│  │  (IClusterCoord.) │      │  (DotNext)       │           │
│  └────────┬──────────┘      └──────────────────┘           │
│           │                                                 │
│           ├──► SingletonTaskRunner (Leader-only tasks)     │
│           ├──► LeaderOnlyBehavior (Reject non-leader)      │
│           ├──► ForwardToLeaderBehavior (Forward requests)  │
│           └──► DistributedLock (Leader-only locking)       │
│                                                             │
│  ┌──────────────────┐                                      │
│  │HttpClusterForward│ (IClusterForwarder)                  │
│  └──────────────────┘                                      │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Requirements

- **DotNext.Net.Cluster** 5.16+
- **Catga** 0.1.0+
- **.NET 8.0+**

## Testing

The package includes comprehensive unit tests:

```bash
dotnet test --filter "FullyQualifiedName~Cluster"
```

## Performance Considerations

1. **Leader Election**: Typical election time is 150-300ms with default settings
2. **Heartbeat Overhead**: ~50ms intervals, minimal network traffic
3. **Request Forwarding**: Adds one HTTP round-trip (~1-10ms on local network)
4. **Persistent State**: Use SSD storage for best performance

## Troubleshooting

### No Leader Elected

- Check network connectivity between nodes
- Verify all nodes have correct member endpoints
- Ensure firewall allows traffic on cluster ports
- Check logs for election timeout issues

### Split Brain

- Ensure odd number of nodes (3, 5, 7)
- Verify network partitions are resolved
- Check that majority of nodes are reachable

### High Latency

- Reduce `ElectionTimeout` and `HeartbeatInterval` for faster elections
- Use persistent storage on fast SSDs
- Consider gRPC forwarder instead of HTTP for lower latency

## License

MIT
