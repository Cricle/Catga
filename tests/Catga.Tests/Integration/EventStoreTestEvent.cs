using System;
using Catga.Abstractions;
using MemoryPack;

namespace Catga.Tests.Integration;

[MemoryPackable]
public partial record EventStoreTestEvent : IEvent
{
    public required long MessageId { get; init; }
    public required string Id { get; init; }
    public required string Data { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
