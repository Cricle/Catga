using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Runtime.CompilerServices;

namespace Catga.Persistence;

/// <summary>
/// Base class for NATS JetStream-based stores with simplified initialization
/// </summary>
/// <remarks>
/// Ensures stream existence by attempting CreateStreamAsync on each EnsureInitializedAsync call, ignoring 'already exists' error.
/// </remarks>
public abstract class NatsJSStoreBase
{
    protected readonly INatsConnection Connection;
    protected readonly INatsJSContext JetStream;
    protected readonly string StreamName;
    protected readonly NatsJSStoreOptions Options;

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
    /// Ensures the JetStream is initialized.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        // 直接尝试创建 Stream；若已存在则忽略
        var config = CreateStreamConfig();
        try
        {
            await JetStream.CreateStreamAsync(config, cancellationToken);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400)
        {
            // Stream already exists, ignore
        }
    }
}
