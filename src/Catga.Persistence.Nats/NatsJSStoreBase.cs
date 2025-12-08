using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence;

/// <summary>Base class for NATS JetStream stores.</summary>
public abstract class NatsJSStoreBase(INatsConnection connection, string streamName, NatsJSStoreOptions? options = null)
{
    protected readonly INatsJSContext JetStream = new NatsJSContext(connection);
    protected readonly string StreamName = streamName;
    protected readonly NatsJSStoreOptions Options = options ?? new() { StreamName = streamName };

    protected abstract string[] GetSubjects();

    protected virtual StreamConfig CreateStreamConfig()
        => Options.CreateDefaultStreamConfig(StreamName, GetSubjects());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask EnsureInitializedAsync(CancellationToken ct = default)
    {
        try { await JetStream.CreateStreamAsync(CreateStreamConfig(), ct); }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400) { }
    }
}
