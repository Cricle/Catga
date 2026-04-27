using System.Collections.Concurrent;
using Catga.Abstractions;
using Catga.Flow.Dsl;

namespace Catga.Flow.HotReload;

/// <summary>
/// Interface for flow registration and retrieval.
/// Follows Open-Closed principle - extend without modifying existing code.
/// </summary>
public interface IFlowRegistry
{
    /// <summary>Register a flow configuration</summary>
    void Register(string flowName, object flowConfig);

    /// <summary>Get a flow configuration by name</summary>
    object? Get(string flowName);

    /// <summary>Get all registered flow names</summary>
    IEnumerable<string> GetAll();

    /// <summary>Check if a flow is registered</summary>
    bool Contains(string flowName);

    /// <summary>Unregister a flow</summary>
    bool Unregister(string flowName);
}

/// <summary>
/// Interface for flow hot reload functionality.
/// </summary>
public interface IFlowReloader
{
    /// <summary>Reload a flow configuration</summary>
    ValueTask ReloadAsync(string flowName, object newConfig, CancellationToken ct = default);

    /// <summary>Event raised when a flow is reloaded</summary>
    event EventHandler<FlowReloadedEvent>? FlowReloaded;
}

/// <summary>
/// Interface for flow version management.
/// </summary>
public interface IFlowVersionManager
{
    /// <summary>Get the current version of a flow</summary>
    int GetCurrentVersion(string flowName);

    /// <summary>Set the version of a flow</summary>
    void SetVersion(string flowName, int version);

    /// <summary>Increment and return new version</summary>
    int IncrementVersion(string flowName);
}

/// <summary>
/// Event raised when a flow is reloaded.
/// </summary>
public sealed record FlowReloadedEvent
{
    public required string FlowName { get; init; }
    public required int OldVersion { get; init; }
    public required int NewVersion { get; init; }
    public required DateTime ReloadedAt { get; init; }
}

/// <summary>
/// Default flow registry implementation using concurrent dictionary.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class FlowRegistry : IFlowRegistry
{
    private readonly ConcurrentDictionary<string, object> _flows = new();

    public void Register(string flowName, object flowConfig)
    {
        ArgumentException.ThrowIfNullOrEmpty(flowName);
        ArgumentNullException.ThrowIfNull(flowConfig);
        _flows[flowName] = flowConfig;
    }

    public object? Get(string flowName)
    {
        _flows.TryGetValue(flowName, out var config);
        return config;
    }

    public IEnumerable<string> GetAll() => _flows.Keys;

    public bool Contains(string flowName) => _flows.ContainsKey(flowName);

    public bool Unregister(string flowName) => _flows.TryRemove(flowName, out _);
}

/// <summary>
/// Default flow version manager implementation.
/// </summary>
public sealed class FlowVersionManager : IFlowVersionManager
{
    private readonly ConcurrentDictionary<string, int> _versions = new();

    public int GetCurrentVersion(string flowName)
    {
        return _versions.GetValueOrDefault(flowName, 0);
    }

    public void SetVersion(string flowName, int version)
    {
        _versions[flowName] = version;
    }

    public int IncrementVersion(string flowName)
    {
        return _versions.AddOrUpdate(flowName, 1, (_, v) => v + 1);
    }
}

/// <summary>
/// Default flow reloader implementation.
/// Supports hot reload with version tracking and event notification.
/// </summary>
public sealed class FlowReloader : IFlowReloader
{
    private readonly IFlowRegistry _registry;
    private readonly IFlowVersionManager _versionManager;

    public event EventHandler<FlowReloadedEvent>? FlowReloaded;

    public FlowReloader(IFlowRegistry registry, IFlowVersionManager versionManager)
    {
        _registry = registry;
        _versionManager = versionManager;
    }

    public ValueTask ReloadAsync(string flowName, object newConfig, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var oldVersion = _versionManager.GetCurrentVersion(flowName);
        var newVersion = _versionManager.IncrementVersion(flowName);

        _registry.Register(flowName, newConfig);

        FlowReloaded?.Invoke(this, new FlowReloadedEvent
        {
            FlowName = flowName,
            OldVersion = oldVersion,
            NewVersion = newVersion,
            ReloadedAt = DateTime.UtcNow
        });

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Typed flow registry for type-safe flow access.
/// Uses IFlowBuilder interface for public API.
/// </summary>
public sealed class TypedFlowRegistry<TState> where TState : class, IFlowState
{
    private readonly IFlowRegistry _registry;

    public TypedFlowRegistry(IFlowRegistry registry)
    {
        _registry = registry;
    }

    public void Register(string flowName, IFlowBuilder<TState> flowConfig)
    {
        _registry.Register(flowName, flowConfig);
    }

    public IFlowBuilder<TState>? Get(string flowName)
    {
        return _registry.Get(flowName) as IFlowBuilder<TState>;
    }
}
