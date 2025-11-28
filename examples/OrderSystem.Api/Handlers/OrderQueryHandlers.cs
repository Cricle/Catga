using Catga.Abstractions;
using Catga.Core;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public class GetOrderHandler(IOrderRepository repository) : IRequestHandler<GetOrderQuery, Order?>
{
    public async Task<CatgaResult<Order?>> HandleAsync(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);
        return CatgaResult<Order?>.Success(order);
    }
}
