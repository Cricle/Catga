using Catga;
using Microsoft.AspNetCore.Mvc;
using OrderApi.Commands;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(ICatgaMediator mediator, ILogger<OrdersController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// 创建新订单
    /// </summary>
    /// <param name="command">订单创建命令</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的订单信息</returns>
    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating order for customer {CustomerId}", command.CustomerId);

        var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(
            command,
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// 获取订单详情
    /// </summary>
    /// <param name="orderId">订单ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订单详情</returns>
    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetOrder(
        string orderId,
        CancellationToken cancellationToken)
    {
        var query = new GetOrderQuery { OrderId = orderId };
        var result = await _mediator.SendAsync<GetOrderQuery, OrderDto>(query, cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return NotFound(new { error = result.Error });
    }
}
