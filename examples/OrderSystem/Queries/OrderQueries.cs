using Catga.Abstractions;
using MemoryPack;
using OrderSystem.Models;

namespace OrderSystem.Queries;

[MemoryPackable]
public partial record GetOrderQuery(string OrderId) : IRequest<Order?>
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record GetAllOrdersQuery : IRequest<List<Order>>
{
    public long MessageId { get; init; }
}
