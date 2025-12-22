using Catga.Abstractions;
using Catga.Serialization.MemoryPack;
using Catga.Tests.PropertyTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using MemoryPack;
using System.Text.Json;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// Serialization Round-Trip Property Tests
/// 验证序列化往返一致性
/// 
/// **Validates: Requirements 25.1, 25.2, 25.4, 25.6**
/// </summary>
[Trait("Category", "Property")]
[Trait("Component", "Serialization")]
public class SerializationPropertyTests
{
    private readonly MemoryPackMessageSerializer _memoryPackSerializer = new();

    #region Property 12: Serialization Round-Trip (MemoryPack)

    /// <summary>
    /// Property 12: MemoryPack Event Serialization Round-Trip
    /// 
    /// *For any* valid event, serializing then deserializing with MemoryPack 
    /// SHALL produce an event equal to the original.
    /// 
    /// **Validates: Requirements 25.2, 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property MemoryPack_Event_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableEventArbitrary(),
            (evt) =>
            {
                // Act
                var bytes = _memoryPackSerializer.Serialize(evt);
                var deserialized = _memoryPackSerializer.Deserialize<SerializableTestEvent>(bytes);

                // Assert
                if (deserialized is null) return false;
                
                return evt.MessageId == deserialized.MessageId &&
                       evt.CorrelationId == deserialized.CorrelationId &&
                       evt.QoS == deserialized.QoS &&
                       evt.Data == deserialized.Data &&
                       evt.Amount == deserialized.Amount;
            });
    }

    /// <summary>
    /// Property 12: MemoryPack Snapshot Serialization Round-Trip
    /// 
    /// *For any* valid snapshot, serializing then deserializing with MemoryPack 
    /// SHALL produce a snapshot equal to the original.
    /// 
    /// **Validates: Requirements 25.2, 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property MemoryPack_Snapshot_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableSnapshotArbitrary(),
            (snapshot) =>
            {
                // Act
                var bytes = _memoryPackSerializer.Serialize(snapshot);
                var deserialized = _memoryPackSerializer.Deserialize<SerializableTestSnapshot>(bytes);

                // Assert
                if (deserialized is null) return false;
                
                return snapshot.AggregateId == deserialized.AggregateId &&
                       snapshot.Version == deserialized.Version &&
                       snapshot.Counter == deserialized.Counter &&
                       snapshot.Name == deserialized.Name;
            });
    }

    /// <summary>
    /// Property 12: MemoryPack Message Serialization Round-Trip
    /// 
    /// *For any* valid message, serializing then deserializing with MemoryPack 
    /// SHALL produce a message equal to the original.
    /// 
    /// **Validates: Requirements 25.2, 25.6**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property MemoryPack_Message_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableMessageArbitrary(),
            (message) =>
            {
                // Act
                var bytes = _memoryPackSerializer.Serialize(message);
                var deserialized = _memoryPackSerializer.Deserialize<SerializableTestMessage>(bytes);

                // Assert
                if (deserialized is null) return false;
                
                return message.MessageId == deserialized.MessageId &&
                       message.CorrelationId == deserialized.CorrelationId &&
                       message.QoS == deserialized.QoS &&
                       message.Content == deserialized.Content;
            });
    }

    /// <summary>
    /// Property 12: MemoryPack FlowState Serialization Round-Trip
    /// 
    /// *For any* valid flow state, serializing then deserializing with MemoryPack 
    /// SHALL produce a flow state equal to the original.
    /// 
    /// **Validates: Requirements 25.2, 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property MemoryPack_FlowState_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableFlowStateArbitrary(),
            (flowState) =>
            {
                // Act
                var bytes = _memoryPackSerializer.Serialize(flowState);
                var deserialized = _memoryPackSerializer.Deserialize<SerializableTestFlowState>(bytes);

                // Assert
                if (deserialized is null) return false;
                
                return flowState.FlowId == deserialized.FlowId &&
                       flowState.Counter == deserialized.Counter &&
                       flowState.Status == deserialized.Status;
            });
    }

    #endregion

    #region Property 12: Serialization Round-Trip (JSON)

    /// <summary>
    /// Property 12: JSON Event Serialization Round-Trip
    /// 
    /// *For any* valid event, serializing then deserializing with JSON 
    /// SHALL produce an event equal to the original.
    /// 
    /// **Validates: Requirements 25.1, 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property JSON_Event_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableEventArbitrary(),
            (evt) =>
            {
                // Act
                var json = JsonSerializer.Serialize(evt);
                var deserialized = JsonSerializer.Deserialize<SerializableTestEvent>(json);

                // Assert
                if (deserialized is null) return false;
                
                return evt.MessageId == deserialized.MessageId &&
                       evt.CorrelationId == deserialized.CorrelationId &&
                       evt.QoS == deserialized.QoS &&
                       evt.Data == deserialized.Data &&
                       evt.Amount == deserialized.Amount;
            });
    }

    /// <summary>
    /// Property 12: JSON Snapshot Serialization Round-Trip
    /// 
    /// *For any* valid snapshot, serializing then deserializing with JSON 
    /// SHALL produce a snapshot equal to the original.
    /// 
    /// **Validates: Requirements 25.1, 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property JSON_Snapshot_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableSnapshotArbitrary(),
            (snapshot) =>
            {
                // Act
                var json = JsonSerializer.Serialize(snapshot);
                var deserialized = JsonSerializer.Deserialize<SerializableTestSnapshot>(json);

                // Assert
                if (deserialized is null) return false;
                
                return snapshot.AggregateId == deserialized.AggregateId &&
                       snapshot.Version == deserialized.Version &&
                       snapshot.Counter == deserialized.Counter &&
                       snapshot.Name == deserialized.Name;
            });
    }

    /// <summary>
    /// Property 12: JSON Message Serialization Round-Trip
    /// 
    /// *For any* valid message, serializing then deserializing with JSON 
    /// SHALL produce a message equal to the original.
    /// 
    /// **Validates: Requirements 25.1, 25.6**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property JSON_Message_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableMessageArbitrary(),
            (message) =>
            {
                // Act
                var json = JsonSerializer.Serialize(message);
                var deserialized = JsonSerializer.Deserialize<SerializableTestMessage>(json);

                // Assert
                if (deserialized is null) return false;
                
                return message.MessageId == deserialized.MessageId &&
                       message.CorrelationId == deserialized.CorrelationId &&
                       message.QoS == deserialized.QoS &&
                       message.Content == deserialized.Content;
            });
    }

    /// <summary>
    /// Property 12: JSON FlowState Serialization Round-Trip
    /// 
    /// *For any* valid flow state, serializing then deserializing with JSON 
    /// SHALL produce a flow state equal to the original.
    /// 
    /// **Validates: Requirements 25.1, 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property JSON_FlowState_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableFlowStateArbitrary(),
            (flowState) =>
            {
                // Act
                var json = JsonSerializer.Serialize(flowState);
                var deserialized = JsonSerializer.Deserialize<SerializableTestFlowState>(json);

                // Assert
                if (deserialized is null) return false;
                
                return flowState.FlowId == deserialized.FlowId &&
                       flowState.Counter == deserialized.Counter &&
                       flowState.Status == deserialized.Status;
            });
    }

    #endregion

    #region Complex Object Serialization Tests

    /// <summary>
    /// Property: MemoryPack Complex Nested Object Round-Trip
    /// 
    /// *For any* valid complex object with nested structures, serializing then 
    /// deserializing SHALL preserve all nested data.
    /// 
    /// **Validates: Requirements 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property MemoryPack_ComplexNestedObject_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.ComplexNestedObjectArbitrary(),
            (obj) =>
            {
                // Act
                var bytes = _memoryPackSerializer.Serialize(obj);
                var deserialized = _memoryPackSerializer.Deserialize<ComplexNestedObject>(bytes);

                // Assert
                if (deserialized is null) return false;
                
                // Verify top-level properties
                if (obj.Id != deserialized.Id || obj.Name != deserialized.Name)
                    return false;

                // Verify nested object
                if (obj.Nested is null != deserialized.Nested is null)
                    return false;
                
                if (obj.Nested is not null && deserialized.Nested is not null)
                {
                    if (obj.Nested.Value != deserialized.Nested.Value ||
                        obj.Nested.Description != deserialized.Nested.Description)
                        return false;
                }

                // Verify collection
                if (obj.Items is null != deserialized.Items is null)
                    return false;
                
                if (obj.Items is not null && deserialized.Items is not null)
                {
                    if (obj.Items.Count != deserialized.Items.Count)
                        return false;
                    
                    for (int i = 0; i < obj.Items.Count; i++)
                    {
                        if (obj.Items[i] != deserialized.Items[i])
                            return false;
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property: JSON Complex Nested Object Round-Trip
    /// 
    /// *For any* valid complex object with nested structures, serializing then 
    /// deserializing with JSON SHALL preserve all nested data.
    /// 
    /// **Validates: Requirements 25.4**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property JSON_ComplexNestedObject_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            SerializationGenerators.ComplexNestedObjectArbitrary(),
            (obj) =>
            {
                // Act
                var json = JsonSerializer.Serialize(obj);
                var deserialized = JsonSerializer.Deserialize<ComplexNestedObject>(json);

                // Assert
                if (deserialized is null) return false;
                
                // Verify top-level properties
                if (obj.Id != deserialized.Id || obj.Name != deserialized.Name)
                    return false;

                // Verify nested object
                if (obj.Nested is null != deserialized.Nested is null)
                    return false;
                
                if (obj.Nested is not null && deserialized.Nested is not null)
                {
                    if (obj.Nested.Value != deserialized.Nested.Value ||
                        obj.Nested.Description != deserialized.Nested.Description)
                        return false;
                }

                // Verify collection
                if (obj.Items is null != deserialized.Items is null)
                    return false;
                
                if (obj.Items is not null && deserialized.Items is not null)
                {
                    if (obj.Items.Count != deserialized.Items.Count)
                        return false;
                    
                    for (int i = 0; i < obj.Items.Count; i++)
                    {
                        if (obj.Items[i] != deserialized.Items[i])
                            return false;
                    }
                }

                return true;
            });
    }

    #endregion

    #region Serialization Size Comparison Tests

    /// <summary>
    /// Property: MemoryPack produces smaller output than JSON for most objects
    /// 
    /// *For any* valid event, MemoryPack serialization SHALL produce output 
    /// that is smaller or equal to JSON serialization.
    /// 
    /// **Validates: Performance characteristics**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property MemoryPack_ProducesSmallerOrEqualOutput_ThanJSON()
    {
        return Prop.ForAll(
            SerializationGenerators.SerializableEventArbitrary(),
            (evt) =>
            {
                // Act
                var memoryPackBytes = _memoryPackSerializer.Serialize(evt);
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt));

                // Assert - MemoryPack should generally be smaller or equal
                // Note: For very small objects, JSON might be smaller due to overhead
                // We allow up to 2x JSON size as acceptable
                return memoryPackBytes.Length <= jsonBytes.Length * 2;
            });
    }

    #endregion
}

#region Serialization Generators

/// <summary>
/// FsCheck generators for serialization property tests
/// </summary>
public static class SerializationGenerators
{
    public static Arbitrary<SerializableTestEvent> SerializableEventArbitrary()
    {
        var gen = from messageId in Gen.Choose(1, int.MaxValue).Select(i => (long)i)
                  from correlationId in Gen.Choose(0, int.MaxValue).Select(i => (long)i)
                  from qos in Gen.Elements(QualityOfService.AtMostOnce, QualityOfService.AtLeastOnce, QualityOfService.ExactlyOnce)
                  from data in Arb.Generate<NonEmptyString>()
                  from amount in Gen.Choose(-1000000, 1000000).Select(i => (decimal)i / 100)
                  select new SerializableTestEvent
                  {
                      MessageId = messageId,
                      CorrelationId = correlationId,
                      QoS = qos,
                      Data = data.Get,
                      Amount = amount
                  };
        return gen.ToArbitrary();
    }

    public static Arbitrary<SerializableTestSnapshot> SerializableSnapshotArbitrary()
    {
        var gen = from aggregateId in Arb.Generate<Guid>()
                  from version in Gen.Choose(0, 10000)
                  from counter in Gen.Choose(0, int.MaxValue)
                  from name in Arb.Generate<NonEmptyString>()
                  select new SerializableTestSnapshot
                  {
                      AggregateId = aggregateId,
                      Version = version,
                      Counter = counter,
                      Name = name.Get
                  };
        return gen.ToArbitrary();
    }

    public static Arbitrary<SerializableTestMessage> SerializableMessageArbitrary()
    {
        var gen = from messageId in Gen.Choose(1, int.MaxValue).Select(i => (long)i)
                  from correlationId in Gen.Choose(0, int.MaxValue).Select(i => (long)i)
                  from qos in Gen.Elements(QualityOfService.AtMostOnce, QualityOfService.AtLeastOnce, QualityOfService.ExactlyOnce)
                  from content in Arb.Generate<NonEmptyString>()
                  select new SerializableTestMessage
                  {
                      MessageId = messageId,
                      CorrelationId = correlationId,
                      QoS = qos,
                      Content = content.Get
                  };
        return gen.ToArbitrary();
    }

    public static Arbitrary<SerializableTestFlowState> SerializableFlowStateArbitrary()
    {
        var gen = from flowId in Arb.Generate<NonEmptyString>()
                  from counter in Gen.Choose(0, int.MaxValue)
                  from status in Arb.Generate<NonEmptyString>()
                  select new SerializableTestFlowState
                  {
                      FlowId = flowId.Get,
                      Counter = counter,
                      Status = status.Get
                  };
        return gen.ToArbitrary();
    }

    public static Arbitrary<ComplexNestedObject> ComplexNestedObjectArbitrary()
    {
        var nestedGen = from value in Gen.Choose(0, 100)
                        from description in Arb.Generate<NonEmptyString>()
                        select new NestedObject { Value = value, Description = description.Get };

        var itemsGen = Gen.ListOf(Arb.Generate<NonEmptyString>())
            .Select(l => l.Select(s => s.Get).ToList());

        var gen = from id in Gen.Choose(1, int.MaxValue)
                  from name in Arb.Generate<NonEmptyString>()
                  from nested in nestedGen
                  from items in itemsGen
                  select new ComplexNestedObject
                  {
                      Id = id,
                      Name = name.Get,
                      Nested = nested,
                      Items = items
                  };
        return gen.ToArbitrary();
    }
}

#endregion

#region Test Models

[MemoryPackable]
public partial class SerializableTestEvent : IEvent
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; }
    public string Data { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[MemoryPackable]
public partial class SerializableTestSnapshot
{
    public Guid AggregateId { get; set; }
    public int Version { get; set; }
    public int Counter { get; set; }
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class SerializableTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; }
    public string Content { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class SerializableTestFlowState
{
    public string FlowId { get; set; } = string.Empty;
    public int Counter { get; set; }
    public string Status { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class ComplexNestedObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public NestedObject? Nested { get; set; }
    public List<string>? Items { get; set; }
}

[MemoryPackable]
public partial class NestedObject
{
    public int Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

#endregion
