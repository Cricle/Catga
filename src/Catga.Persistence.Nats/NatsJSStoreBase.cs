using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Runtime.CompilerServices;

namespace Catga.Persistence;

/// <summary>
/// Base class for NATS JetStream-based stores with lock-free initialization
/// </summary>
/// <remarks>
/// Uses CAS (Compare-And-Swap) for lock-free initialization.
/// No IAsyncDisposable needed - fully lock-free design.
/// </remarks>
public abstract class NatsJSStoreBase
{
    protected readonly INatsConnection Connection;
    protected readonly INatsJSContext JetStream;
    protected readonly string StreamName;
    protected readonly NatsJSStoreOptions Options;

    private volatile int _initializationState; // 0=未开始, 1=初始化中, 2=已完成
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
    /// Ensures the JetStream is initialized using lock-free CAS pattern.
    /// Fast path (already initialized) has zero overhead.
    /// </summary>
    /// <remarks>
    /// Lock-free implementation using Interlocked.CompareExchange (CAS).
    /// Multiple threads can safely call this method concurrently.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: 已初始化则直接返回（零开销）
        // volatile read 确保可见性
        if (_initialized) return;

        // Slow path: 需要初始化
        await InitializeSlowPathAsync(cancellationToken);
    }

    private async ValueTask InitializeSlowPathAsync(CancellationToken cancellationToken)
    {
        // CAS: 只有一个线程能成功从 0 -> 1
        if (Interlocked.CompareExchange(ref _initializationState, 1, 0) == 0)
        {
            try
            {
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
                // 标记初始化完成
                Interlocked.Exchange(ref _initializationState, 2);
            }
            catch
            {
                // 重置状态允许重试
                Interlocked.Exchange(ref _initializationState, 0);
                throw;
            }
        }
        else
        {
            // 等待初始化完成（自旋等待）
            // 使用 SpinWait 优化 CPU 使用
            var spinner = new SpinWait();
            while (Volatile.Read(ref _initializationState) == 1)
            {
                spinner.SpinOnce();
            }

            // 如果初始化失败，当前线程也抛出异常
            if (!_initialized)
                throw new InvalidOperationException("Stream initialization failed by another thread.");
        }
    }
}

