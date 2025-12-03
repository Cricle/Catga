using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.EventSourcing;

/// <summary>
/// Event versioning support for schema evolution.
/// AOT-compatible design.
/// </summary>
public interface IEventUpgrader
{
    /// <summary>Event type this upgrader handles.</summary>
    Type SourceType { get; }

    /// <summary>Target type after upgrade.</summary>
    Type TargetType { get; }

    /// <summary>Source version.</summary>
    int SourceVersion { get; }

    /// <summary>Target version.</summary>
    int TargetVersion { get; }

    /// <summary>Upgrade event to newer version.</summary>
    IEvent Upgrade(IEvent source);
}

/// <summary>
/// Typed event upgrader base class.
/// </summary>
public abstract class EventUpgrader<TSource, TTarget> : IEventUpgrader
    where TSource : class, IEvent
    where TTarget : class, IEvent
{
    public Type SourceType => typeof(TSource);
    public Type TargetType => typeof(TTarget);
    public abstract int SourceVersion { get; }
    public abstract int TargetVersion { get; }

    public IEvent Upgrade(IEvent source) => UpgradeCore((TSource)source);

    protected abstract TTarget UpgradeCore(TSource source);
}

/// <summary>
/// Event version registry for managing event schema evolution.
/// </summary>
public interface IEventVersionRegistry
{
    /// <summary>Register an event upgrader.</summary>
    void Register(IEventUpgrader upgrader);

    /// <summary>Get current version for event type.</summary>
    int GetCurrentVersion<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>() where TEvent : IEvent;

    /// <summary>Upgrade event to latest version.</summary>
    IEvent UpgradeToLatest(IEvent @event, int fromVersion);

    /// <summary>Check if event type has upgraders.</summary>
    bool HasUpgraders(Type eventType);
}

/// <summary>
/// Default event version registry implementation.
/// </summary>
public sealed class EventVersionRegistry : IEventVersionRegistry
{
    private readonly Dictionary<Type, List<IEventUpgrader>> _upgraders = new();
    private readonly Dictionary<Type, int> _currentVersions = new();

    public void Register(IEventUpgrader upgrader)
    {
        if (!_upgraders.TryGetValue(upgrader.SourceType, out var list))
        {
            list = new List<IEventUpgrader>(4);
            _upgraders[upgrader.SourceType] = list;
        }

        list.Add(upgrader);
        list.Sort((a, b) => a.SourceVersion.CompareTo(b.SourceVersion));

        // Track highest version
        if (!_currentVersions.TryGetValue(upgrader.TargetType, out var current) ||
            upgrader.TargetVersion > current)
        {
            _currentVersions[upgrader.TargetType] = upgrader.TargetVersion;
        }
    }

    public int GetCurrentVersion<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>() where TEvent : IEvent
    {
        return _currentVersions.TryGetValue(typeof(TEvent), out var version) ? version : 1;
    }

    public IEvent UpgradeToLatest(IEvent @event, int fromVersion)
    {
        var current = @event;
        var currentVersion = fromVersion;
        var maxIterations = 100; // Prevent infinite loops

        for (var i = 0; i < maxIterations; i++)
        {
            var eventType = current.GetType();
            if (!_upgraders.TryGetValue(eventType, out var upgraders))
                break;

            var upgraded = false;
            foreach (var upgrader in upgraders)
            {
                if (upgrader.SourceVersion == currentVersion)
                {
                    current = upgrader.Upgrade(current);
                    currentVersion = upgrader.TargetVersion;
                    upgraded = true;
                    break; // Re-check with new type
                }
            }

            if (!upgraded)
                break;
        }

        return current;
    }

    public bool HasUpgraders(Type eventType) => _upgraders.ContainsKey(eventType);
}

/// <summary>
/// Attribute to mark event version.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class EventVersionAttribute : Attribute
{
    public int Version { get; }

    public EventVersionAttribute(int version)
    {
        Version = version;
    }
}

/// <summary>
/// Versioned stored event with schema version.
/// </summary>
public sealed class VersionedStoredEvent
{
    public long Version { get; init; }
    public IEvent Event { get; init; } = null!;
    public DateTime Timestamp { get; init; }
    public string EventType { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
}
