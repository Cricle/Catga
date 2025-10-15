using Catga.Core;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Results;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Get order handler - no try-catch needed
/// </summary>
public class GetOrderHandler : SafeRequestHandler<GetOrderQuery, Order?>
{
    private readonly IOrderRepository _repository;

    public GetOrderHandler(IOrderRepository repository, ILogger<GetOrderHandler> logger) : base(logger)
    {
        _repository = repository;
    }

    protected override async Task<Order?> HandleCoreAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        Logger.LogDebug("Order query: {OrderId}, found: {Found}", request.OrderId, order != null);
        return order;
    }
}

/// <summary>
/// Get customer orders handler - no try-catch needed
/// </summary>
public class GetCustomerOrdersHandler : SafeRequestHandler<GetCustomerOrdersQuery, List<Order>>
{
    private readonly IOrderRepository _repository;

    public GetCustomerOrdersHandler(IOrderRepository repository, ILogger<GetCustomerOrdersHandler> logger) : base(logger)
    {
        _repository = repository;
    }

    protected override async Task<List<Order>> HandleCoreAsync(
        GetCustomerOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var orders = await _repository.GetByCustomerIdAsync(
            request.CustomerId,
            request.PageIndex,
            request.PageSize,
            cancellationToken);

        Logger.LogDebug("Customer orders query: {CustomerId}, count: {Count}",
            request.CustomerId, orders.Count);

        return orders;
    }
}

