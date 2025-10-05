using Catga.Handlers;
using Catga.Results;
using OrderApi.Commands;
using OrderApi.Services;

namespace OrderApi.Handlers;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ILogger<CreateOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<CatgaResult<CreateOrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 验证产品
            var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
            if (product == null)
            {
                return CatgaResult<CreateOrderResult>.Failure("Product not found");
            }

            // 检查库存
            if (product.Stock < request.Quantity)
            {
                return CatgaResult<CreateOrderResult>.Failure("Insufficient stock");
            }

            // 创建订单
            var order = new Order
            {
                OrderId = Guid.NewGuid().ToString("N")[..8].ToUpper(),
                CustomerId = request.CustomerId,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                TotalAmount = product.Price * request.Quantity,
                CreatedAt = DateTime.UtcNow
            };

            await _orderRepository.AddAsync(order, cancellationToken);

            // 更新库存
            product.Stock -= request.Quantity;
            await _productRepository.UpdateAsync(product, cancellationToken);

            _logger.LogInformation("Order created: {OrderId}", order.OrderId);

            return CatgaResult<CreateOrderResult>.Success(new CreateOrderResult
            {
                OrderId = order.OrderId,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order");
            return CatgaResult<CreateOrderResult>.Failure("Failed to create order");
        }
    }
}

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<CatgaResult<OrderDto>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order == null)
        {
            return CatgaResult<OrderDto>.Failure("Order not found");
        }

        var dto = new OrderDto
        {
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            ProductId = order.ProductId,
            Quantity = order.Quantity,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            CreatedAt = order.CreatedAt
        };

        return CatgaResult<OrderDto>.Success(dto);
    }
}
