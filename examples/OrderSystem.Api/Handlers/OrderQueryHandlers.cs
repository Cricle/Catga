using Catga;
using Catga.Abstractions;
using Catga.Core;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

[CatgaHandler]
[Route("/orders/{orderId}", Method = "GET")]
public partial class GetOrderHandler(IOrderRepository repository) : IRequestHandler<GetOrderQuery, Order?>
{
    private async Task<CatgaResult<Order?>> HandleAsyncCore(GetOrderQuery request, CancellationToken ct)
    {
        var order = await repository.GetByIdAsync(request.OrderId, ct);
        return CatgaResult<Order?>.Success(order);
    }
}
