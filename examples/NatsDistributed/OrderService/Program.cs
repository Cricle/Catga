using Catga.DependencyInjection;
using Catga.Nats.DependencyInjection;
using Catga.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using OrderService.Commands;
using OrderService.Handlers;
using OrderService.Models;
using OrderService.Services;

var builder = Host.CreateApplicationBuilder(args);

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 配置 NATS
var natsOptions = new NatsOptions
{
    Url = "nats://localhost:4222",
    Name = "OrderService",
    MaxReconnect = 10,
    ReconnectWait = TimeSpan.FromSeconds(2)
};

// 注册服务
builder.Services.AddSingleton(natsOptions);
builder.Services.AddNatsCore(natsOptions);

// 添加 Catga 和 NATS 集成
builder.Services.AddTransit();
builder.Services.AddNatsCatga(options =>
{
    options.ServiceId = "order-service";
    options.EnableRequestReply = true;
    options.EnableEventPublishing = true;
});

// 注册应用服务
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();

// 注册处理器
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetOrderQuery, OrderDto>, GetOrderHandler>();

// 启动服务
var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("订单服务启动中...");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "订单服务运行出错");
}
