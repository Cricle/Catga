namespace Catga.Abstractions;

/// <summary>
/// Ambient correlation context for the current async execution context.
/// Uses AsyncLocal for thread-safe, scope-isolated storage.
/// </summary>
public interface ICorrelationContext
{
    long? Current { get; }
    void Set(long correlationId);
    void Clear();
}

/// <summary>
/// AsyncLocal-based correlation context. Isolated per async call chain.
/// Safe to register as singleton — each async flow has its own value.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<long?> _current = new();

    public long? Current => _current.Value;
    public void Set(long correlationId) => _current.Value = correlationId;
    public void Clear() => _current.Value = null;
}
