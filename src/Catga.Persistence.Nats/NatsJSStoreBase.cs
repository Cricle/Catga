using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Runtime.CompilerServices;

namespace Catga.Persistence;

/// <summary>
/// Base class for NATS JetStream-based stores with optimized initialization
/// </summary>
public abstract class NatsJSStoreBase : IAsyncDisposable
{
    protected readonly INatsConnection Connection;
    protected readonly INatsJSContext JetStream;
    protected readonly string StreamName;
    protected readonly NatsJSStoreOptions Options;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    protected NatsJSStoreBase(
        INatsConnection connection,
        string streamName,
        NatsJSStoreOptions? options = null)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        StreamName = streamName;
        Options = options ?? new NatsJSStoreOptions { StreamName = streamName };
        JetStream = new NatsJSContext(connection);
    }

    /// <summary>
    /// Get the subjects pattern for this store
    /// </summary>
    protected abstract string[] GetSubjects();

    /// <summary>
    /// Create the JetStream configuration for this store using options
    /// </summary>
    protected virtual StreamConfig CreateStreamConfig()
    {
        return Options.CreateStreamConfig(StreamName, GetSubjects());
    }

    /// <summary>
    /// Ensures the JetStream is initialized using double-checked locking pattern.
    /// Fast path (already initialized) has zero lock overhead.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: 已初始化则直接返回（零锁开销）
        // volatile read 确保可见性
        if (_initialized) return;

        // Slow path: 需要初始化
        await InitializeSlowPathAsync(cancellationToken);
    }

    private async ValueTask InitializeSlowPathAsync(CancellationToken cancellationToken)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：防止多次初始化
            if (_initialized) return;

            var config = CreateStreamConfig();

            try
            {
                await JetStream.CreateStreamAsync(config, cancellationToken);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 400)
            {
                // Stream already exists, ignore
            }

            // volatile write 确保初始化完成对其他线程可见
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

