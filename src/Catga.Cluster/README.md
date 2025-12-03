# Catga.Cluster

Distributed cluster support for Catga using DotNext Raft consensus.

## Features

- **IClusterCoordinator** - Simple API for leader election and coordination
- **SingletonTaskRunner** - Run background tasks only on leader node
- **LeaderOnlyBehavior** - Pipeline behavior to reject non-leader requests
- **ForwardToLeaderBehavior** - Pipeline behavior to forward requests to leader
- **DistributedLock** - Leader-only distributed locking

## Usage

### 1. Add DotNext Raft Cluster

First, configure DotNext Raft cluster (e.g., using `DotNext.AspNetCore.Cluster`):

```csharp
builder.Services.JoinCluster();  // DotNext cluster setup
```

### 2. Add Catga Cluster

```csharp
builder.Services.AddCatgaCluster();
```

### 3. Use Leader-Only Commands

```csharp
// Option 1: Fail if not leader
builder.Services.AddLeaderOnlyBehavior<MyCommand, MyResult>();

// Option 2: Forward to leader
builder.Services.AddForwardToLeaderBehavior<MyCommand, MyResult>();
```

### 4. Singleton Background Tasks

```csharp
public class OutboxProcessor : SingletonTaskRunner
{
    protected override string TaskName => "OutboxProcessor";

    protected override async Task ExecuteLeaderTaskAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Process outbox - only runs on leader
            await ProcessPendingMessagesAsync(ct);
            await Task.Delay(1000, ct);
        }
    }
}

// Register
builder.Services.AddSingletonTask<OutboxProcessor>();
```

### 5. Check Leadership

```csharp
public class MyService
{
    private readonly IClusterCoordinator _coordinator;

    public async Task DoSomethingAsync()
    {
        if (_coordinator.IsLeader)
        {
            // Execute leader-only logic
        }

        // Or use ExecuteIfLeader for safe execution
        var success = await _coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await DoLeaderWorkAsync(ct);
        });
    }
}
```

## Requirements

- DotNext.Net.Cluster 5.16+
- Catga 0.1.0+
