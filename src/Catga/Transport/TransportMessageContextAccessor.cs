using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Transport;

internal static class TransportMessageContextAccessor
{
    private static readonly AsyncLocal<TransportContext?> CurrentContext = new();
    private static readonly ConcurrentDictionary<Type, Action<object, long>?> MessageIdSetters = new();
    private static readonly ConcurrentDictionary<Type, Func<object, long>?> MessageIdGetters = new();
    private static readonly ConcurrentDictionary<Type, Action<object, long?>?> CorrelationIdSetters = new();
    private static readonly ConcurrentDictionary<Type, Func<object, long?>?> CorrelationIdGetters = new();

    public static TransportContext? Current => CurrentContext.Value;

    public static IDisposable Push(TransportContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    public static TransportContext EnrichOutgoing<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TransportContext? context)
        where TMessage : class
        => EnrichOutgoing<TMessage>(message: null, context);

    public static TransportContext EnrichOutgoing<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage? message, TransportContext? context)
        where TMessage : class
    {
        var effective = context ?? TransportContextFactory.CreateDefault<TMessage>();
        var ambient = CurrentContext.Value;

        if (!effective.CorrelationId.HasValue && ambient?.CorrelationId.HasValue == true)
            effective = effective with { CorrelationId = ambient.Value.CorrelationId };

        if (ambient?.Metadata is { Count: > 0 })
        {
            if (effective.Metadata is null)
            {
                effective = effective with
                {
                    Metadata = new Dictionary<string, string>(ambient.Value.Metadata!, StringComparer.Ordinal)
                };
            }
            else
            {
                var mergedMetadata = new Dictionary<string, string>(ambient.Value.Metadata!, StringComparer.Ordinal);
                foreach (var (key, value) in effective.Metadata)
                    mergedMetadata[key] = value;

                effective = effective with { Metadata = mergedMetadata };
            }
        }

        if (message is IPrioritizedMessage prioritizedMessage)
        {
            const string priorityKey = "x-priority";
            effective = AddMetadataIfMissing(effective, priorityKey, ((int)prioritizedMessage.Priority).ToString());
        }

        if (message is IDelayedMessage delayedMessage)
        {
            const string delayKey = "x-delay";
            var delay = delayedMessage.DeliverAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                var totalMilliseconds = delay.TotalMilliseconds >= int.MaxValue
                    ? int.MaxValue
                    : Math.Max(1, (int)Math.Ceiling(delay.TotalMilliseconds));
                effective = AddMetadataIfMissing(effective, delayKey, totalMilliseconds.ToString());
            }
        }

        if (!effective.MessageId.HasValue)
            effective = effective with { MessageId = MessageExtensions.NewMessageId() };

        if (string.IsNullOrEmpty(effective.MessageType))
            effective = effective with { MessageType = TypeNameCache<TMessage>.FullName };

        if (!effective.SentAt.HasValue)
            effective = effective with { SentAt = DateTime.UtcNow };

        return effective;
    }

    [UnconditionalSuppressMessage("AOT", "IL2111", Justification = "Cached accessors are created from the runtime message type and used only for known message contracts.")]
    public static void ApplyToMessage(object message, TransportContext context)
    {
        var type = message.GetType();

        if (context.MessageId.HasValue)
            MessageIdSetters.GetOrAdd(type, CreateMessageIdSetter)?.Invoke(message, context.MessageId.Value);

        if (context.CorrelationId.HasValue)
            CorrelationIdSetters.GetOrAdd(type, CreateCorrelationIdSetter)?.Invoke(message, context.CorrelationId);
    }

    [UnconditionalSuppressMessage("AOT", "IL2111", Justification = "Cached accessors are created from the runtime message type and used only for known message contracts.")]
    public static long? TryGetMessageId(object? message)
    {
        if (message is null) return null;
        var getter = MessageIdGetters.GetOrAdd(message.GetType(), CreateMessageIdGetter);
        return getter?.Invoke(message);
    }

    [UnconditionalSuppressMessage("AOT", "IL2111", Justification = "Cached accessors are created from the runtime message type and used only for known message contracts.")]
    public static long? TryGetCorrelationId(object? message)
    {
        if (message is null) return null;
        var getter = CorrelationIdGetters.GetOrAdd(message.GetType(), CreateCorrelationIdGetter);
        return getter?.Invoke(message);
    }

    private static Action<object, long>? CreateMessageIdSetter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        var property = type.GetProperty("MessageId", BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite != true || property.PropertyType != typeof(long))
            return null;

        return (instance, value) =>
        {
            var current = (long)(property.GetValue(instance) ?? 0L);
            if (current == 0)
                property.SetValue(instance, value);
        };
    }

    private static Func<object, long>? CreateMessageIdGetter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        var property = type.GetProperty("MessageId", BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead != true || property.PropertyType != typeof(long))
            return null;

        return instance => (long)(property.GetValue(instance) ?? 0L);
    }

    private static Action<object, long?>? CreateCorrelationIdSetter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        var property = type.GetProperty("CorrelationId", BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite != true)
            return null;

        if (property.PropertyType == typeof(long?))
        {
            return (instance, value) =>
            {
                var current = (long?)property.GetValue(instance);
                if (!current.HasValue || current.Value == 0)
                    property.SetValue(instance, value);
            };
        }

        if (property.PropertyType == typeof(long))
        {
            return (instance, value) =>
            {
                if (!value.HasValue) return;
                var current = (long)(property.GetValue(instance) ?? 0L);
                if (current == 0)
                    property.SetValue(instance, value.Value);
            };
        }

        return null;
    }

    private static Func<object, long?>? CreateCorrelationIdGetter(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        var property = type.GetProperty("CorrelationId", BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead != true)
            return null;

        if (property.PropertyType == typeof(long?))
            return instance => (long?)property.GetValue(instance);

        if (property.PropertyType == typeof(long))
            return instance =>
            {
                var value = (long)(property.GetValue(instance) ?? 0L);
                return value == 0 ? null : value;
            };

        return null;
    }

    private sealed class Scope(TransportContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }

    private static TransportContext AddMetadataIfMissing(TransportContext context, string key, string value)
    {
        if (context.Metadata is null)
        {
            return context with
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [key] = value
                }
            };
        }

        if (context.Metadata.ContainsKey(key))
            return context;

        var metadata = new Dictionary<string, string>(context.Metadata, StringComparer.Ordinal)
        {
            [key] = value
        };
        return context with { Metadata = metadata };
    }
}
