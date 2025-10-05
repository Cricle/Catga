using Catga.DependencyInjection;
using Catga.Nats.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NotificationService.Events;
using NotificationService.Handlers;

var builder = Host.CreateApplicationBuilder(args);

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 配置 NATS
var natsOptions = new NatsOptions
{
    Url = "nats://localhost:4222",
    Name = "NotificationService",
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
    options.ServiceId = "notification-service";
    options.EnableEventSubscription = true;
});

// 注册事件处理器  
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedNotificationHandler>();
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedLogHandler>();

// 启动服务
var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("通知服务启动中...");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "通知服务运行出错");
}