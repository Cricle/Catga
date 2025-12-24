using Catga.Abstractions;
using Catga.Core;
using OrderSystem.Models;
using OrderSystem.Queries;

namespace OrderSystem.Handlers;

public sealed class GetOrderHandler(OrderStore store) : IRequestHandler<GetOrderQuery, Order?>
{
    public ValueTask<CatgaResult<Order?>> HandleAsync(GetOrderQuery query, CancellationToken ct = default)
        => ValueTask.FromResult(CatgaResult<Order?>.Success(store.Get(query.OrderId)));
}

public sealed class GetAllOrdersHandler(OrderStore store) : IRequestHandler<GetAllOrdersQuery, List<Order>>
{
    public ValueTask<CatgaResult<List<Order>>> HandleAsync(GetAllOrdersQuery query, CancellationToken ct = default)
        => ValueTask.FromResult(CatgaResult<List<Order>>.Success(store.GetAll()));
}
