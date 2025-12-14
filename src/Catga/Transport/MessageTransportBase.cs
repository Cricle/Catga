using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Transport;

/// <summary>
/// Base class for message transport implementations.
/// Provides common QoS handling, diagnostics, and resilience patterns.
/// </summary>
public abstract class MessageTransportBase : IMessageTransport
{
    protected readonly IMessageSerializer Serializer;
    protected readonly IResiliencePipelineProvider ResilienceProvider;

    public abstract string Name { get; }
    public virtual BatchTransportOptions? BatchOptions => null;
    public virtual CompressionTransportOptions? CompressionOptions => null;

    protected MessageTransportBase(IMessageSerializer serializer, IResiliencePipelineProvider resilienceProvider)
    {
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        ResilienceProvider = resilienceProvider ?? throw new ArgumentNullException(nameof(resilienceProvider));
    }

    #region IMessageTransport Implementation

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var ctx = TransportContextFactory.GetOrCreate<TMessage>(context);
        var qos = GetQoS(message);

        using var activity = StartPublishActivity<TMessage>(ctx);

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                await PublishAtMostOnceAsync(message, ctx, cancellationToken).ConfigureAwait(false);
                break;
            case QualityOfService.AtLeastOnce:
                await PublishAtLeastOnceAsync(message, ctx, cancellationToken).ConfigureAwait(false);
                break;
            case QualityOfService.ExactlyOnce:
                await PublishExactlyOnceAsync(message, ctx, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public abstract Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
        where TMessage : class;

    public abstract Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default)
        where TMessage : class;

    public virtual async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        foreach (var message in messages)
        {
            await PublishAsync(message, context, cancellationToken).ConfigureAwait(false);
        }
    }

    public virtual async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        foreach (var message in messages)
        {
            await SendAsync(message, destination, context, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region QoS Handling

    protected virtual async Task PublishAtMostOnceAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TransportContext context, CancellationToken ct)
        where TMessage : class
    {
        try
        {
            await PublishCoreAsync(message, context, ct).ConfigureAwait(false);
        }
        catch
        {
            // QoS 0: Best-effort, discard on failure
        }
    }

    protected virtual async Task PublishAtLeastOnceAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TransportContext context, CancellationToken ct)
        where TMessage : class
    {
        await ResilienceProvider.ExecuteTransportPublishAsync(
            async token => { await PublishCoreAsync(message, context, token).ConfigureAwait(false); }, ct).ConfigureAwait(false);
    }

    protected virtual async Task PublishExactlyOnceAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TransportContext context, CancellationToken ct)
        where TMessage : class
    {
        // Default: same as AtLeastOnce, override for dedup
        await PublishAtLeastOnceAsync(message, context, ct).ConfigureAwait(false);
    }

    /// <summary>Core publish implementation. Override in derived classes.</summary>
    protected abstract Task PublishCoreAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TransportContext context, CancellationToken ct)
        where TMessage : class;

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static QualityOfService GetQoS<TMessage>(TMessage message)
    {
        return message switch
        {
            IEvent ev => ev.QoS,
            IMessage msg => msg.QoS,
            _ => QualityOfService.AtLeastOnce
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Activity? StartPublishActivity<TMessage>(TransportContext context)
    {
        var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, Name);
            activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, TypeNameCache<TMessage>.Name);
            activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "publish");
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
            if (context.MessageId.HasValue)
                activity.SetTag(CatgaActivitySource.Tags.MessageId, context.MessageId.Value);
        }
        return activity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string GetDestination<TMessage>(Func<Type, string>? naming = null)
    {
        return naming != null ? naming(typeof(TMessage)) : TypeNameCache<TMessage>.Name;
    }

    #endregion
}
