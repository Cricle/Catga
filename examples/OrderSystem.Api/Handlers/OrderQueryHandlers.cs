using Catga.Abstractions;
using Catga.Core;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public class GetOrderHandler : IRequestHandler<GetOrderQuery, Order?>
{
    private readonly IOrderRepository _repository;

    public GetOrderHandler(IOrderRepository repository)
        => _repository = repository;

    public async Task<CatgaResult<Order?>> HandleAsync(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        return CatgaResult<Order?>.Success(order);
    }
}
