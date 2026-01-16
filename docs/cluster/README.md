# Catga Cluster Guide

Complete guide to running Catga in a distributed cluster using Raft consensus.

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Configuration](#configuration)
4. [Leader Election](#leader-election)
5. [Request Forwarding](#request-forwarding)
6. [Singleton Tasks](#singleton-tasks)
7. [Distributed Locking](#distributed-locking)
8. [Health Checks](#health-checks)
9. [Production Deployment](#production-deployment)
10. [Troubleshooting](#troubleshooting)

## Overview

Catga.Cluster provides distributed coordination using the Raft consensus algorithm via DotNext.Net.Cluster. This enables:

- **Leader Election**: Automatic leader election with fast failover
- **Request Forwarding**: Automatic forwarding of commands to the leader node
- **Singleton Tasks**: Background tasks that run only on the leader
- **Distributed Locking**: Cluster-wide locks for critical operations
- **High Availability**: Automatic failover when leader fails

### Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Node 0    │────▶│   Node 1    │────▶│   Node 2    │
│  (Leader)   │◀────│  (Follower) │◀────│  (Follower) │
└─────────────┘     └─────────────┘     └─────────────┘
      │                    │                    │
      └────────────────────┴────────────────────┘
                    Raft Consensus
                    
- Leader handles all writes
- Followers replicate state
- Automatic failover on leader failure
- Requires majority (quorum) for operations
```

## Quick Start

### 1. Install Package

```bash
dotnet add package Catga.Cluster
```

### 2. Configure Cluster

Add to `appsettings.json`:

```json
{
  "Cluster": {
    "LocalNodeEndpoint": "http://localhost:5000",
    "Members": [
      "http://localhost:5001",
      "http://localhost:5002"
    ],
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

var builder = WebApplication.CreateBuilder(args);

// Load cluster configuration
var clusterConfig = RaftClusterConfiguration.FromConfiguration(builder.Configuration);

// Configure DotNext Raft
builder.Services.UseInMemoryConfigurationStorage()
    .JoinCluster(clusterConfig.LocalNodeEndpoint, clusterConfig.Members);

// Add Catga cluster
builder.Services.AddCatgaCluster();

// Add health checks
builder.Services.AddHealthChecks()
    .AddClusterHealthCheck();
```

### 4. Run Cluster

**Using provided scripts:**

```bash
# PowerShell (Windows)
./examples/OrderSystem/run-cluster.ps1

# Bash (Linux/Mac)
chmod +x ./examples/OrderSystem/run-cluster.sh
./examples/OrderSystem/run-cluster.sh
```

**Manual (3 terminals):**

```bash
# Terminal 1
dotnet run --project examples/OrderSystem -- --cluster --port 5000 --node-id node0

# Terminal 2
dotnet run --project examples/OrderSystem -- --cluster --port 5001 --node-id node1

# Terminal 3
dotnet run --project examples/OrderSystem -- --cluster --port 5002 --node-id node2
```

## Configuration

### Cluster Settings

| Setting | Description | Default | Recommended |
|---------|-------------|---------|-------------|
| `LocalNodeEndpoint` | This node's HTTP endpoint | Required | `http://hostname:port` |
| `Members` | Other cluster member endpoints | Required | Array of endpoints |
| `ElectionTimeout` | Time before starting election | `150ms` | `150-300ms` |
| `HeartbeatInterval` | Leader heartbeat frequency | `50ms` | `50-100ms` |
| `PersistentStatePath` | Path for Raft state storage | `null` (in-memory) | `./raft-state` |

### Environment Variables

For containerized deployments:

```bash
Cluster__LocalNodeEndpoint=http://node0:5000
Cluster__Members__0=http://node1:5000
Cluster__Members__1=http://node2:5000
Cluster__ElectionTimeout=00:00:00.150
Cluster__HeartbeatInterval=00:00:00.050
Cluster__PersistentStatePath=/data/raft-state
```

### Cluster Size Recommendations

| Nodes | Fault Tolerance | Use Case |
|-------|----------------|----------|
| 1 | None | Development only |
| 3 | 1 node failure | Small production |
| 5 | 2 node failures | Medium production |
| 7 | 3 node failures | Large production |

**Always use odd numbers** to avoid split-brain scenarios.

## Leader Election

### How It Works

1. **Initial State**: All nodes start as followers
2. **Election Timeout**: If no heartbeat received, node becomes candidate
3. **Vote Request**: Candidate requests votes from other nodes
4. **Majority Wins**: Node with majority votes becomes leader
5. **Heartbeats**: Leader sends periodic heartbeats to maintain leadership

### Checking Leadership

```csharp
public class MyService
{
    private readonly IClusterCoordinator _coordinator;

    public void CheckLeadership()
    {
        if (_coordinator.IsLeader)
        {
            Console.WriteLine("This node is the leader");
        }
        else
        {
            Console.WriteLine($"Leader is at: {_coordinator.LeaderEndpoint}");
        }
    }
}
```

### Leadership Events

```csharp
public class MyService
{
    public MyService(IClusterCoordinator coordinator)
    {
        coordinator.LeadershipChanged += isLeader =>
        {
            if (isLeader)
            {
                Console.WriteLine("This node became leader");
                // Start leader-only tasks
            }
            else
            {
                Console.WriteLine("This node lost leadership");
                // Stop leader-only tasks
            }
        };
    }
}
```

## Request Forwarding

### Leader-Only Behavior

Reject requests on non-leader nodes:

```csharp
// In Program.cs
builder.Services.AddLeaderOnlyBehavior<CreateOrderCommand, OrderResult>();

// Requests on non-leader nodes will fail with:
// "This node is not the leader. Leader: http://leader:5000"
```

### Forward-to-Leader Behavior

Automatically forward requests to leader:

```csharp
// In Program.cs
builder.Services.AddForwardToLeaderBehavior<CreateOrderCommand, OrderResult>();

// Requests on non-leader nodes are automatically forwarded via HTTP
```

### Custom Forwarder

Implement `IClusterForwarder` for custom logic:

```csharp
public class GrpcClusterForwarder : IClusterForwarder
{
    private readonly GrpcChannel _channel;

    public async Task<CatgaResult<TResponse>> ForwardAsync<TRequest, TResponse>(
        TRequest request,
        string leaderEndpoint,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        // Custom gRPC forwarding
        var client = new CommandService.CommandServiceClient(_channel);
        var response = await client.ExecuteAsync(request, cancellationToken: ct);
        return CatgaResult<TResponse>.Success(response);
    }
}

// Register
builder.Services.AddSingleton<IClusterForwarder, GrpcClusterForwarder>();
```

## Singleton Tasks

Background tasks that run only on the leader node.

### Basic Example

```csharp
public class OutboxProcessor : SingletonTaskRunner
{
    private readonly IOutboxStore _store;
    private readonly IMessageBus _bus;

    public OutboxProcessor(
        IClusterCoordinator coordinator,
        IOutboxStore store,
        IMessageBus bus,
        ILogger<OutboxProcessor> logger)
        : base(coordinator, logger, checkInterval: TimeSpan.FromSeconds(1))
    {
        _store = store;
        _bus = bus;
    }

    protected override string TaskName => "OutboxProcessor";

    protected override async Task ExecuteLeaderTaskAsync(CancellationToken ct)
    {
        _logger.LogInformation("OutboxProcessor started on leader");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var messages = await _store.GetPendingAsync(100, ct);
                
                foreach (var msg in messages)
                {
                    await _bus.PublishAsync(msg, ct);
                    await _store.MarkProcessedAsync(msg.Id, ct);
                }

                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break; // Leadership lost or shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox");
                await Task.Delay(5000, ct);
            }
        }

        _logger.LogInformation("OutboxProcessor stopped");
    }
}

// Register
builder.Services.AddSingletonTask<OutboxProcessor>();
```

### Behavior

- **Automatic Start**: Task starts when node becomes leader
- **Automatic Stop**: Task stops when leadership is lost
- **Cancellation**: `CancellationToken` is cancelled on leadership loss
- **Restart**: Task automatically restarts if node regains leadership

## Distributed Locking

Cluster-wide locks for critical operations.

### Basic Usage

```csharp
public class CriticalService
{
    private readonly IClusterCoordinator _coordinator;

    public async Task<bool> ExecuteCriticalOperationAsync()
    {
        // Try to acquire lock (only succeeds on leader)
        await using var lockHandle = await DistributedLock.TryAcquireAsync(
            _coordinator,
            resource: "critical-operation",
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

### Lock Characteristics

- **Leader-Only**: Only the leader can acquire locks
- **Automatic Release**: Lock released when disposed or leadership lost
- **Timeout**: Configurable timeout for lock acquisition
- **Validation**: `IsValid` property checks if lock is still held

## Health Checks

### Register Health Check

```csharp
builder.Services.AddHealthChecks()
    .AddClusterHealthCheck();
```

### Health Check Response

**Healthy (Leader):**
```json
{
  "status": "Healthy",
  "results": {
    "cluster": {
      "status": "Healthy",
      "description": "This node is the leader",
      "data": {
        "NodeId": "node0",
        "IsLeader": true,
        "LeaderEndpoint": "http://localhost:5000"
      }
    }
  }
}
```

**Healthy (Follower):**
```json
{
  "status": "Healthy",
  "results": {
    "cluster": {
      "status": "Healthy",
      "description": "Cluster has a leader",
      "data": {
        "NodeId": "node1",
        "IsLeader": false,
        "LeaderEndpoint": "http://localhost:5000"
      }
    }
  }
}
```

**Unhealthy (No Leader):**
```json
{
  "status": "Unhealthy",
  "results": {
    "cluster": {
      "status": "Unhealthy",
      "description": "No leader elected in cluster",
      "data": {
        "NodeId": "node1",
        "IsLeader": false,
        "LeaderEndpoint": "none"
      }
    }
  }
}
```

## Production Deployment

### Docker Compose Example

```yaml
version: '3.8'

services:
  node0:
    image: myapp:latest
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_URLS=http://+:5000
      - Cluster__LocalNodeEndpoint=http://node0:5000
      - Cluster__Members__0=http://node1:5000
      - Cluster__Members__1=http://node2:5000
      - Cluster__PersistentStatePath=/data/raft-state
    volumes:
      - node0-data:/data
    networks:
      - catga-cluster
    restart: unless-stopped

  node1:
    image: myapp:latest
    ports:
      - "5001:5000"
    environment:
      - ASPNETCORE_URLS=http://+:5000
      - Cluster__LocalNodeEndpoint=http://node1:5000
      - Cluster__Members__0=http://node0:5000
      - Cluster__Members__1=http://node2:5000
      - Cluster__PersistentStatePath=/data/raft-state
    volumes:
      - node1-data:/data
    networks:
      - catga-cluster
    restart: unless-stopped

  node2:
    image: myapp:latest
    ports:
      - "5002:5000"
    environment:
      - ASPNETCORE_URLS=http://+:5000
      - Cluster__LocalNodeEndpoint=http://node2:5000
      - Cluster__Members__0=http://node0:5000
      - Cluster__Members__1=http://node1:5000
      - Cluster__PersistentStatePath=/data/raft-state
    volumes:
      - node2-data:/data
    networks:
      - catga-cluster
    restart: unless-stopped

volumes:
  node0-data:
  node1-data:
  node2-data:

networks:
  catga-cluster:
    driver: bridge
```

### Kubernetes Example

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: catga-cluster
spec:
  serviceName: catga
  replicas: 3
  selector:
    matchLabels:
      app: catga
  template:
    metadata:
      labels:
        app: catga
    spec:
      containers:
      - name: catga
        image: myapp:latest
        ports:
        - containerPort: 5000
          name: http
        env:
        - name: POD_NAME
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        - name: Cluster__LocalNodeEndpoint
          value: "http://$(POD_NAME).catga:5000"
        - name: Cluster__Members__0
          value: "http://catga-cluster-0.catga:5000"
        - name: Cluster__Members__1
          value: "http://catga-cluster-1.catga:5000"
        - name: Cluster__Members__2
          value: "http://catga-cluster-2.catga:5000"
        - name: Cluster__PersistentStatePath
          value: "/data/raft-state"
        volumeMounts:
        - name: data
          mountPath: /data
  volumeClaimTemplates:
  - metadata:
      name: data
    spec:
      accessModes: [ "ReadWriteOnce" ]
      resources:
        requests:
          storage: 10Gi
---
apiVersion: v1
kind: Service
metadata:
  name: catga
spec:
  clusterIP: None
  selector:
    app: catga
  ports:
  - port: 5000
    name: http
```

### Best Practices

1. **Persistent Storage**: Always use persistent volumes for `PersistentStatePath`
2. **Network Reliability**: Ensure low-latency, reliable network between nodes
3. **Monitoring**: Monitor leader elections, heartbeat failures, and split-brain scenarios
4. **Graceful Shutdown**: Allow time for leadership transfer on shutdown
5. **Backup**: Regularly backup Raft state directory
6. **Security**: Use TLS for inter-node communication in production

## Troubleshooting

### No Leader Elected

**Symptoms:**
- Health check shows "No leader elected"
- All nodes stuck in candidate state

**Solutions:**
1. Check network connectivity between all nodes
2. Verify all nodes have correct member endpoints
3. Ensure firewall allows traffic on cluster ports
4. Check logs for election timeout issues
5. Verify system clocks are synchronized (NTP)

### Split Brain

**Symptoms:**
- Multiple nodes think they are leader
- Inconsistent state across nodes

**Solutions:**
1. Ensure odd number of nodes (3, 5, 7)
2. Verify network partitions are resolved
3. Check that majority of nodes are reachable
4. Review network configuration and firewall rules

### High Latency

**Symptoms:**
- Slow leader elections
- High request latency
- Frequent leadership changes

**Solutions:**
1. Reduce `ElectionTimeout` (but not below 100ms)
2. Reduce `HeartbeatInterval` (but not below 25ms)
3. Use SSD storage for `PersistentStatePath`
4. Ensure low-latency network between nodes
5. Consider gRPC forwarder instead of HTTP

### Frequent Leader Changes

**Symptoms:**
- Leader changes multiple times per minute
- Singleton tasks keep restarting

**Solutions:**
1. Increase `ElectionTimeout` to 200-300ms
2. Check for network instability
3. Monitor CPU and memory usage on nodes
4. Verify no nodes are being killed/restarted
5. Check for clock skew between nodes

### State Corruption

**Symptoms:**
- Node fails to start
- Raft state errors in logs

**Solutions:**
1. Stop all nodes
2. Backup current state directories
3. Delete corrupted state directory
4. Restart cluster (node will rejoin and sync)
5. If all nodes corrupted, restore from backup

## Performance Tuning

### Election Timeout

- **Lower (100-150ms)**: Faster failover, more elections
- **Higher (200-300ms)**: Fewer elections, slower failover
- **Recommended**: 150ms for local network, 300ms for WAN

### Heartbeat Interval

- **Lower (25-50ms)**: Faster failure detection, more network traffic
- **Higher (100-200ms)**: Less network traffic, slower failure detection
- **Recommended**: 50ms (1/3 of election timeout)

### Persistent State

- **In-Memory**: Fastest, but state lost on restart
- **SSD**: Good balance of speed and durability
- **HDD**: Slowest, not recommended for production

### Request Forwarding

- **HTTP**: Simple, works everywhere, ~1-10ms overhead
- **gRPC**: Lower latency, ~0.5-5ms overhead, requires implementation
- **Direct**: No forwarding, client must find leader

## Monitoring

### Key Metrics

1. **Leader Elections**: Count and frequency
2. **Heartbeat Failures**: Failed heartbeats from leader
3. **Request Forwarding**: Forwarded request count and latency
4. **Singleton Task Restarts**: How often tasks restart due to leadership changes
5. **Raft Log Size**: Size of persistent state

### Logging

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "DotNext.Net.Cluster": "Debug",
      "Catga.Cluster": "Debug"
    }
  }
}
```

### Alerts

Set up alerts for:
- No leader elected for > 5 seconds
- More than 10 leader elections per hour
- Raft state directory > 1GB
- Heartbeat failures > 10% of attempts

## License

MIT
