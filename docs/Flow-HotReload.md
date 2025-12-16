# Flow Hot Reload

Flow Hot Reload allows dynamic registration and updating of flow configurations at runtime without restarting the application.

## Features

- **Dynamic Flow Registration**: Register and unregister flows at runtime
- **Version Management**: Track flow configuration versions
- **Reload Events**: Get notified when flows are reloaded
- **Thread-Safe**: All operations are thread-safe

## Quick Start

### 1. Add Services

```csharp
services.AddFlowHotReload();
```

### 2. Register a Flow

```csharp
var registry = serviceProvider.GetRequiredService<IFlowRegistry>();
registry.Register("OrderFlow", orderFlowBuilder);
```

### 3. Reload a Flow

```csharp
var reloader = serviceProvider.GetRequiredService<IFlowReloader>();
reloader.FlowReloaded += (sender, e) =>
{
    Console.WriteLine($"Flow {e.FlowName} reloaded: v{e.OldVersion} -> v{e.NewVersion}");
};

await reloader.ReloadAsync("OrderFlow", newOrderFlowBuilder);
```

## Interfaces

### IFlowRegistry

```csharp
public interface IFlowRegistry
{
    void Register(string flowName, object flowConfig);
    object? Get(string flowName);
    bool Unregister(string flowName);
    bool Contains(string flowName);
    IEnumerable<string> GetAll();
}
```

### IFlowVersionManager

```csharp
public interface IFlowVersionManager
{
    int GetCurrentVersion(string flowName);
    void SetVersion(string flowName, int version);
    int IncrementVersion(string flowName);
}
```

### IFlowReloader

```csharp
public interface IFlowReloader
{
    event EventHandler<FlowReloadedEvent>? FlowReloaded;
    ValueTask ReloadAsync(string flowName, object newConfig, CancellationToken ct = default);
}
```

## Typed Flow Registry

For type-safe access to flow builders:

```csharp
services.AddTypedFlowRegistry<OrderState>();

var typedRegistry = serviceProvider.GetRequiredService<TypedFlowRegistry<OrderState>>();
typedRegistry.Register("OrderFlow", orderFlowBuilder);

IFlowBuilder<OrderState>? builder = typedRegistry.Get("OrderFlow");
```

## Best Practices

1. **Use version checking** before executing flows to ensure you're running the latest version
2. **Subscribe to FlowReloaded** events for logging and monitoring
3. **Use TypedFlowRegistry** when you need type safety
4. **Register flows at startup** and reload as needed
