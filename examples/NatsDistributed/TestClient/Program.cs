using Catga;
using Catga.DependencyInjection;
using Catga.Nats.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using TestClient.Commands;

// 创建主机构建器
var builder = Host.CreateApplicationBuilder(args);

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 配置 NATS
var natsUrl = "nats://localhost:4222";
var natsOpts = NatsOpts.Default with
{
    Url = natsUrl,
    Name = "TestClient"
};

// 注册服务
builder.Services.AddSingleton<INatsConnection>(sp =>
    new NatsConnection(natsOpts));

// 添加 Catga 和 NATS 集成
builder.Services.AddCatga();
builder.Services.AddNatsCatga(natsUrl);

// 构建主机
var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var mediator = host.Services.GetRequiredService<ICatgaMediator>();

logger.LogInformation("🚀 测试客户端启动");
logger.LogInformation("等待连接到 NATS...");

try
{
    // 等待一下确保服务都启动了
    await Task.Delay(2000);

    await RunTestScenariosAsync(mediator, logger);
}
catch (Exception ex)
{
    logger.LogError(ex, "测试客户端运行出错");
}
finally
{
    logger.LogInformation("测试客户端结束");
}

static async Task RunTestScenariosAsync(ICatgaMediator mediator, ILogger logger)
{
    logger.LogInformation("========================================");
    logger.LogInformation("开始执行测试场景");
    logger.LogInformation("========================================");

    // 场景 1: 创建订单 - 成功案例
    await TestCreateOrderSuccessAsync(mediator, logger);

    await Task.Delay(1000);

    // 场景 2: 创建订单 - 库存不足
    await TestCreateOrderInsufficientStockAsync(mediator, logger);

    await Task.Delay(1000);

    // 场景 3: 创建订单 - 产品不存在
    await TestCreateOrderProductNotFoundAsync(mediator, logger);

    await Task.Delay(1000);

    // 场景 4: 查询订单
    await TestQueryOrderAsync(mediator, logger);

    logger.LogInformation("========================================");
    logger.LogInformation("所有测试场景执行完成");
    logger.LogInformation("========================================");
}

static async Task TestCreateOrderSuccessAsync(ICatgaMediator mediator, ILogger logger)
{
    logger.LogInformation("📝 场景1: 创建订单 - 成功案例");

    try
    {
        var command = new CreateOrderCommand
        {
            CustomerId = "CUST-001",
            ProductId = "PROD-001", // 笔记本电脑
            Quantity = 1
        };

        logger.LogInformation("发送创建订单命令: 客户={CustomerId}, 产品={ProductId}, 数量={Quantity}",
            command.CustomerId, command.ProductId, command.Quantity);

        var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        if (result.IsSuccess)
        {
            logger.LogInformation("✅ 订单创建成功!");
            logger.LogInformation("   订单ID: {OrderId}", result.Value.OrderId);
            logger.LogInformation("   总金额: ¥{TotalAmount}", result.Value.TotalAmount);
            logger.LogInformation("   状态: {Status}", result.Value.Status);
            logger.LogInformation("   创建时间: {CreatedAt}", result.Value.CreatedAt);
        }
        else
        {
            logger.LogWarning("❌ 订单创建失败: {Error}", result.Error);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ 场景1执行出错");
    }
}

static async Task TestCreateOrderInsufficientStockAsync(ICatgaMediator mediator, ILogger logger)
{
    logger.LogInformation("📝 场景2: 创建订单 - 库存不足");

    try
    {
        var command = new CreateOrderCommand
        {
            CustomerId = "CUST-002",
            ProductId = "PROD-001", // 笔记本电脑
            Quantity = 999 // 超过库存数量
        };

        logger.LogInformation("发送创建订单命令: 客户={CustomerId}, 产品={ProductId}, 数量={Quantity} (超过库存)",
            command.CustomerId, command.ProductId, command.Quantity);

        var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        if (result.IsSuccess)
        {
            logger.LogWarning("⚠️ 意外成功创建订单: {OrderId}", result.Value.OrderId);
        }
        else
        {
            logger.LogInformation("✅ 正确拒绝订单: {Error}", result.Error);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ 场景2执行出错");
    }
}

static async Task TestCreateOrderProductNotFoundAsync(ICatgaMediator mediator, ILogger logger)
{
    logger.LogInformation("📝 场景3: 创建订单 - 产品不存在");

    try
    {
        var command = new CreateOrderCommand
        {
            CustomerId = "CUST-003",
            ProductId = "PROD-999", // 不存在的产品
            Quantity = 1
        };

        logger.LogInformation("发送创建订单命令: 客户={CustomerId}, 产品={ProductId} (不存在), 数量={Quantity}",
            command.CustomerId, command.ProductId, command.Quantity);

        var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        if (result.IsSuccess)
        {
            logger.LogWarning("⚠️ 意外成功创建订单: {OrderId}", result.Value.OrderId);
        }
        else
        {
            logger.LogInformation("✅ 正确拒绝订单: {Error}", result.Error);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ 场景3执行出错");
    }
}

static async Task TestQueryOrderAsync(ICatgaMediator mediator, ILogger logger)
{
    logger.LogInformation("📝 场景4: 查询订单信息");

    try
    {
        // 首先创建一个订单
        var createCommand = new CreateOrderCommand
        {
            CustomerId = "CUST-004",
            ProductId = "PROD-002", // 无线鼠标
            Quantity = 2
        };

        var createResult = await mediator.SendAsync<CreateOrderCommand, OrderResult>(createCommand);

        if (createResult.IsSuccess)
        {
            var orderId = createResult.Value.OrderId;
            logger.LogInformation("创建测试订单成功: {OrderId}", orderId);

            // 查询订单
            var query = new GetOrderQuery { OrderId = orderId };
            var queryResult = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);

            if (queryResult.IsSuccess)
            {
                var order = queryResult.Value;
                logger.LogInformation("✅ 查询订单成功!");
                logger.LogInformation("   订单ID: {OrderId}", order.OrderId);
                logger.LogInformation("   客户ID: {CustomerId}", order.CustomerId);
                logger.LogInformation("   产品: {ProductName} (ID: {ProductId})", order.ProductName, order.ProductId);
                logger.LogInformation("   数量: {Quantity}", order.Quantity);
                logger.LogInformation("   单价: ¥{UnitPrice}", order.UnitPrice);
                logger.LogInformation("   总金额: ¥{TotalAmount}", order.TotalAmount);
                logger.LogInformation("   状态: {Status}", order.Status);
                logger.LogInformation("   创建时间: {CreatedAt}", order.CreatedAt);
            }
            else
            {
                logger.LogWarning("❌ 查询订单失败: {Error}", queryResult.Error);
            }
        }
        else
        {
            logger.LogWarning("❌ 创建测试订单失败: {Error}", createResult.Error);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ 场景4执行出错");
    }
}
