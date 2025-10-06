# 🎯 Catga 示例

Catga 是一个简洁、高性能的 CQRS 框架，专注于核心功能。

---

## 📚 快速开始

查看项目根目录的 [QUICK_START.md](../QUICK_START.md) 快速上手。

---

## 💡 简单示例

### 1. 基础 CQRS

```csharp
using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 配置服务
var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddCatga();
});

var app = builder.Build();
var mediator = app.Services.GetRequiredService<ICatgaMediator>();

// Command
var command = new CreateOrderCommand
{
    OrderId = Guid.NewGuid().ToString(),
    CustomerId = "CUST-001",
    Amount = 199.99m
};

var result = await mediator.SendAsync(command);
if (result.IsSuccess)
{
    Console.WriteLine($"订单创建成功: {result.Value!.OrderId}");
}

// 消息定义
public class CreateOrderCommand : IRequest<OrderDto>
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

public record OrderDto(string OrderId, string CustomerId, decimal Amount);

// 处理器
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    public Task<CatgaResult<OrderDto>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var order = new OrderDto(
            request.OrderId,
            request.CustomerId,
            request.Amount
        );

        return Task.FromResult(CatgaResult<OrderDto>.Success(order));
    }
}
```

---

## 📖 更多示例

查看文档和测试代码获取更多示例：

| 资源 | 说明 |
|-----|------|
| [QUICK_START.md](../QUICK_START.md) | 快速开始指南 |
| [ARCHITECTURE.md](../ARCHITECTURE.md) | 完整架构说明 |
| [README.md](../README.md) | 项目主页 |
| [tests/](../tests/) | 单元测试（最佳示例） |

---

**保持简洁，专注核心！** ✨
