using System;
using System.Collections.Concurrent;
using Catga.Abstractions;

namespace Catga.Generated;

/// <summary>
/// Reflection-free event type registry for deserialization.
/// Populated by source generator via ModuleInitializer calls.
/// </summary>
public static class EventTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Func<byte[], IMessageSerializer, IEvent?>> _deserializers = new();
    private static readonly ConcurrentDictionary<string, Func<object, IMessageSerializer, byte[]>> _serializers = new();

    public static void Register<TEvent>() where TEvent : IEvent
    {
        var t = typeof(TEvent);
        var key = t.FullName!;
        _deserializers[key] = (data, serializer) => (IEvent?)serializer.Deserialize(data, t);
        _serializers[key] = (obj, serializer) => serializer.Serialize(obj, t);
    }

    public static bool TryDeserialize(string typeFullName, byte[] data, IMessageSerializer serializer, out IEvent? evt)
    {
        if (_deserializers.TryGetValue(typeFullName, out var fn))
        {
            evt = fn(data, serializer);
            return evt is not null;
        }
        evt = null;
        return false;
    }

    public static bool TrySerialize(string typeFullName, object evt, IMessageSerializer serializer, out byte[] data)
    {
        if (_serializers.TryGetValue(typeFullName, out var fn))
        {
            data = fn(evt, serializer);
            return true;
        }
        data = Array.Empty<byte>();
        return false;
    }
}
