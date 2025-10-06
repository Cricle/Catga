using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================
// Catga 事件溯源示例
// ============================================================

Console.WriteLine("🎬 Catga 事件溯源示例\n");

var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddEventSourcing();
        services.AddProjection<OrderSummaryProjection>();
    })
    .Build();

var eventStore = host.Services.GetRequiredService<IEventStore>();
var projectionManager = host.Services.GetRequiredService<IProjectionManager>();

// 启动投影管理器
await host.StartAsync();

// ============================================================
// 示例 1: 基础事件溯源
// ============================================================
Console.WriteLine("📝 示例 1: 基础事件溯源");

var orderId = "order-001";

// 创建订单事件
var events = new IEvent[]
{
    new OrderCreatedEvent(orderId, "Customer-123", DateTime.UtcNow),
    new OrderItemAddedEvent(orderId, "Product-A", 2, 29.99m),
    new OrderItemAddedEvent(orderId, "Product-B", 1, 49.99m),
    new OrderConfirmedEvent(orderId, DateTime.UtcNow)
};

// 追加事件到流
await eventStore.AppendToStreamAsync($"order-{orderId}", events);
Console.WriteLine($"  ✅ 已保存 {events.Length} 个事件到流 order-{orderId}");

// 读取事件流
var storedEvents = await eventStore.ReadStreamAsync($"order-{orderId}");
Console.WriteLine($"\n  📖 读取事件流 (共 {storedEvents.Count} 个事件):");
foreach (var evt in storedEvents)
{
    Console.WriteLine($"    - Version {evt.Version}: {evt.EventType}");
}

Console.WriteLine();

// ============================================================
// 示例 2: 聚合根重建
// ============================================================
Console.WriteLine("🔄 示例 2: 从事件流重建聚合根");

// 创建新订单
var order = new Order();
var newOrderEvents = new IEvent[]
{
    new OrderCreatedEvent("order-002", "Customer-456", DateTime.UtcNow),
    new OrderItemAddedEvent("order-002", "Product-C", 3, 19.99m),
    new OrderConfirmedEvent("order-002", DateTime.UtcNow),
    new OrderShippedEvent("order-002", "TRACK-123", DateTime.UtcNow)
};

// 保存事件
await eventStore.AppendToStreamAsync("order-order-002", newOrderEvents);

// 重建聚合根
var loadedEvents = await eventStore.ReadStreamAsync("order-order-002");
var rebuiltOrder = new Order();
rebuiltOrder.LoadFromHistory(loadedEvents.Select(e => DeserializeEvent(e)));

Console.WriteLine($"  订单 ID: {rebuiltOrder.Id}");
Console.WriteLine($"  客户 ID: {rebuiltOrder.CustomerId}");
Console.WriteLine($"  状态: {rebuiltOrder.Status}");
Console.WriteLine($"  版本: {rebuiltOrder.Version}");
Console.WriteLine($"  商品数量: {rebuiltOrder.Items.Count}");

Console.WriteLine();

// ============================================================
// 示例 3: 快照
// ============================================================
Console.WriteLine("📸 示例 3: 快照机制");

// 保存快照
var snapshot = new OrderSnapshot(
    rebuiltOrder.Id,
    rebuiltOrder.CustomerId,
    rebuiltOrder.Status,
    rebuiltOrder.Items.ToList(),
    rebuiltOrder.Total
);

await eventStore.SaveSnapshotAsync($"order-{rebuiltOrder.Id}", rebuiltOrder.Version, snapshot);
Console.WriteLine($"  ✅ 已保存快照 (Version {rebuiltOrder.Version})");

// 加载快照
var (loadedSnapshot, version) = await eventStore.LoadSnapshotAsync<OrderSnapshot>($"order-{rebuiltOrder.Id}");
if (loadedSnapshot != null)
{
    Console.WriteLine($"  📖 已加载快照:");
    Console.WriteLine($"    - Version: {version}");
    Console.WriteLine($"    - 订单 ID: {loadedSnapshot.Id}");
    Console.WriteLine($"    - 状态: {loadedSnapshot.Status}");
    Console.WriteLine($"    - 总金额: ${loadedSnapshot.Total}");
}

Console.WriteLine();

// ============================================================
// 示例 4: 投影（读模型）
// ============================================================
Console.WriteLine("🎯 示例 4: 投影和读模型");
Console.WriteLine("  投影会自动处理所有事件并更新读模型");
Console.WriteLine("  等待 2 秒让投影处理完成...");

await Task.Delay(2000);

Console.WriteLine("  ✅ 投影处理完成（查看日志输出）");

Console.WriteLine();

// ============================================================
// 示例 5: 读取所有事件
// ============================================================
Console.WriteLine("📚 示例 5: 读取所有事件");

var allEvents = new List<StoredEvent>();
await foreach (var evt in eventStore.ReadAllAsync())
{
    allEvents.Add(evt);
}

Console.WriteLine($"  共有 {allEvents.Count} 个事件:");
foreach (var evt in allEvents.Take(5))
{
    Console.WriteLine($"    - Position {evt.Position}: {evt.EventType} (Stream: {evt.StreamId}, Version: {evt.Version})");
}
if (allEvents.Count > 5)
{
    Console.WriteLine($"    ... 还有 {allEvents.Count - 5} 个事件");
}

Console.WriteLine("\n✅ 所有示例完成！");

await host.StopAsync();

// ============================================================
// 辅助方法
// ============================================================
static IEvent DeserializeEvent(StoredEvent storedEvent)
{
    return storedEvent.EventType switch
    {
        var t when t.Contains("OrderCreatedEvent") =>
            System.Text.Json.JsonSerializer.Deserialize<OrderCreatedEvent>(storedEvent.EventData)!,
        var t when t.Contains("OrderItemAddedEvent") =>
            System.Text.Json.JsonSerializer.Deserialize<OrderItemAddedEvent>(storedEvent.EventData)!,
        var t when t.Contains("OrderConfirmedEvent") =>
            System.Text.Json.JsonSerializer.Deserialize<OrderConfirmedEvent>(storedEvent.EventData)!,
        var t when t.Contains("OrderShippedEvent") =>
            System.Text.Json.JsonSerializer.Deserialize<OrderShippedEvent>(storedEvent.EventData)!,
        _ => throw new InvalidOperationException($"Unknown event type: {storedEvent.EventType}")
    };
}

// ============================================================
// 领域模型
// ============================================================

// 订单聚合根
class Order : AggregateRoot
{
    public string CustomerId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; } = OrderStatus.Created;
    public List<OrderItem> Items { get; private set; } = new();
    public decimal Total => Items.Sum(i => i.Quantity * i.Price);

    protected override void Apply(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreatedEvent e:
                Id = e.OrderId;
                CustomerId = e.CustomerId;
                Status = OrderStatus.Created;
                break;

            case OrderItemAddedEvent e:
                Items.Add(new OrderItem(e.ProductId, e.Quantity, e.Price));
                break;

            case OrderConfirmedEvent:
                Status = OrderStatus.Confirmed;
                break;

            case OrderShippedEvent e:
                Status = OrderStatus.Shipped;
                break;
        }
    }
}

enum OrderStatus
{
    Created,
    Confirmed,
    Shipped,
    Delivered
}

record OrderItem(string ProductId, int Quantity, decimal Price);

record OrderSnapshot(
    string Id,
    string CustomerId,
    OrderStatus Status,
    List<OrderItem> Items,
    decimal Total);

// ============================================================
// 事件定义
// ============================================================

record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    DateTime CreatedAt) : IEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = CreatedAt;
    public string? CorrelationId { get; init; }
    public DateTime OccurredAt { get; init; } = CreatedAt;
}

record OrderItemAddedEvent(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price) : IEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

record OrderConfirmedEvent(
    string OrderId,
    DateTime ConfirmedAt) : IEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = ConfirmedAt;
    public string? CorrelationId { get; init; }
    public DateTime OccurredAt { get; init; } = ConfirmedAt;
}

record OrderShippedEvent(
    string OrderId,
    string TrackingNumber,
    DateTime ShippedAt) : IEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = ShippedAt;
    public string? CorrelationId { get; init; }
    public DateTime OccurredAt { get; init; } = ShippedAt;
}

// ============================================================
// 投影（读模型）
// ============================================================

class OrderSummaryProjection : IProjection
{
    private readonly ILogger<OrderSummaryProjection> _logger;
    private int _totalOrders = 0;
    private int _shippedOrders = 0;

    public string ProjectionName => "OrderSummary";

    public OrderSummaryProjection(ILogger<OrderSummaryProjection> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(StoredEvent storedEvent, CancellationToken cancellationToken = default)
    {
        switch (storedEvent.EventType)
        {
            case var t when t.Contains("OrderCreatedEvent"):
                _totalOrders++;
                _logger.LogInformation("📊 投影更新: 总订单数 = {TotalOrders}", _totalOrders);
                break;

            case var t when t.Contains("OrderShippedEvent"):
                _shippedOrders++;
                _logger.LogInformation("📊 投影更新: 已发货订单 = {ShippedOrders}/{TotalOrders}",
                    _shippedOrders, _totalOrders);
                break;
        }

        return Task.CompletedTask;
    }
}

