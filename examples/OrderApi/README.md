# Order API 示例

这是一个使用 Catga 框架构建的简单订单管理 API 示例。

## 功能特性

- ✅ CQRS 模式 - 命令和查询分离
- ✅ 依赖注入 - 使用 ASP.NET Core DI
- ✅ 内存存储 - 用于演示目的
- ✅ 错误处理 - 使用 CatgaResult
- ✅ 日志记录 - 结构化日志
- ✅ Swagger 文档 - API 文档

## 快速开始

### 1. 运行项目

```bash
cd examples/OrderApi
dotnet run
```

### 2. 访问 Swagger UI

打开浏览器访问: `https://localhost:7xxx/swagger`

### 3. 测试 API

#### 创建订单

```bash
curl -X POST "https://localhost:7xxx/api/orders" \
     -H "Content-Type: application/json" \
     -d '{
       "customerId": "CUST-001",
       "productId": "PROD-001",
       "quantity": 2
     }'
```

响应:
```json
{
  "orderId": "A1B2C3D4",
  "totalAmount": 1999.98,
  "createdAt": "2025-10-05T12:00:00Z"
}
```

#### 查询订单

```bash
curl -X GET "https://localhost:7xxx/api/orders/A1B2C3D4"
```

响应:
```json
{
  "orderId": "A1B2C3D4",
  "customerId": "CUST-001",
  "productId": "PROD-001",
  "quantity": 2,
  "totalAmount": 1999.98,
  "status": "Created",
  "createdAt": "2025-10-05T12:00:00Z"
}
```

## 项目结构

```
OrderApi/
├── Controllers/
│   └── OrdersController.cs      # API 控制器
├── Commands/
│   └── OrderCommands.cs         # 命令和查询定义
├── Handlers/
│   └── OrderHandlers.cs         # 命令处理器
├── Services/
│   └── Models.cs                # 数据模型和仓储
└── Program.cs                   # 应用程序入口点
```

## 预置数据

系统预置了以下产品数据：

| 产品ID | 名称 | 价格 | 库存 |
|--------|------|------|------|
| PROD-001 | Laptop | $999.99 | 10 |
| PROD-002 | Mouse | $29.99 | 50 |
| PROD-003 | Keyboard | $79.99 | 25 |

## 架构说明

### CQRS 模式

- **命令 (Commands)**: `CreateOrderCommand` - 改变系统状态
- **查询 (Queries)**: `GetOrderQuery` - 读取数据

### 依赖注入

```csharp
// 注册 Catga
builder.Services.AddCatga();

// 注册处理器
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResult>, CreateOrderHandler>();
```

### 错误处理

```csharp
if (result.IsSuccess)
{
    return Ok(result.Value);
}
return BadRequest(new { error = result.Error });
```

## 扩展示例

### 添加事件处理

```csharp
// 定义事件
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
}

// 处理器
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task<CatgaResult> HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // 发送邮件、更新统计等
        return CatgaResult.Success();
    }
}
```

### 添加验证

```csharp
public record CreateOrderCommand : MessageBase, ICommand<CreateOrderResult>
{
    [Required]
    public string CustomerId { get; init; } = string.Empty;

    [Required]
    public string ProductId { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; init; }
}

// 启用验证
builder.Services.AddCatga(options =>
{
    options.AddValidation();
});
```

## 相关文档

- [Catga 快速开始](../../docs/guides/quick-start.md)
- [API 参考](../../docs/api/)
- [基本使用示例](../../docs/examples/basic-usage.md)
