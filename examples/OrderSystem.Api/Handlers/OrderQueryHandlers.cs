using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public class GetOrderHandler : SafeRequestHandler<GetOrderQuery, Order?>
{
    private readonly IOrderRepository _repository;

    public GetOrderHandler(IOrderRepository repository, ILogger<GetOrderHandler> logger) : base(logger)
        => _repository = repository;

    protected override async Task<Order?> HandleCoreAsync(GetOrderQuery request, CancellationToken cancellationToken)
        => await _repository.GetByIdAsync(request.OrderId, cancellationToken);
}
