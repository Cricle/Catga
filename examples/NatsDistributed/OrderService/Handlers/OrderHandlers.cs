using Catga;
using Catga.Handlers;
using Catga.Results;
using Microsoft.Extensions.Logging;
using OrderService.Commands;
using OrderService.Models;
using OrderService.Services;

namespace OrderService.Handlers;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("处理创建订单命令: {@Command}", request);

            // 获取产品信息
            var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
            if (product == null)
            {
                _logger.LogWarning("产品不存在: {ProductId}", request.ProductId);
                return CatgaResult<OrderResult>.Failure("产品不存在");
            }

            // 检查库存
            if (product.Stock < request.Quantity)
            {
                _logger.LogWarning("库存不足: {ProductId}, 需要: {Quantity}, 现有: {Stock}",
                    request.ProductId, request.Quantity, product.Stock);
                return CatgaResult<OrderResult>.Failure("库存不足");
            }

            // 创建订单
            var order = new Order
            {
                OrderId = Guid.NewGuid().ToString("N")[..8].ToUpper(),
                CustomerId = request.CustomerId,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                UnitPrice = product.Price,
                TotalAmount = product.Price * request.Quantity,
                CreatedAt = DateTime.UtcNow
            };

            await _orderRepository.AddAsync(order, cancellationToken);

            // 更新库存
            product.Stock -= request.Quantity;
            await _productRepository.UpdateAsync(product, cancellationToken);

            _logger.LogInformation("订单创建成功: {OrderId}, 总金额: {TotalAmount}",
                order.OrderId, order.TotalAmount);

            // 发布订单创建事件
            var orderCreatedEvent = new OrderCreatedEvent
            {
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                ProductId = order.ProductId,
                ProductName = product.Name,
                Quantity = order.Quantity,
                TotalAmount = order.TotalAmount
            };

            await _mediator.PublishAsync(orderCreatedEvent, cancellationToken);

            return CatgaResult<OrderResult>.Success(new OrderResult
            {
                OrderId = order.OrderId,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                CreatedAt = order.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建订单失败");
            return CatgaResult<OrderResult>.Failure("创建订单失败");
        }
    }
}

public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<GetOrderHandler> _logger;

    public GetOrderHandler(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ILogger<GetOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<CatgaResult<OrderDto>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("查询订单: {OrderId}", request.OrderId);

            var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
            if (order == null)
            {
                _logger.LogWarning("订单不存在: {OrderId}", request.OrderId);
                return CatgaResult<OrderDto>.Failure("订单不存在");
            }

            // 获取产品信息
            var product = await _productRepository.GetByIdAsync(order.ProductId, cancellationToken);

            var dto = new OrderDto
            {
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                ProductId = order.ProductId,
                ProductName = product?.Name ?? "未知产品",
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                CreatedAt = order.CreatedAt
            };

            return CatgaResult<OrderDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询订单失败: {OrderId}", request.OrderId);
            return CatgaResult<OrderDto>.Failure("查询订单失败");
        }
    }
}
