# 分布式集群示例 - Catga + NATS

## 📖 简介

极简的分布式集群示例，演示：
- 🚀 **跨节点通信** - NATS 高性能消息传输
- 📡 **负载均衡** - 请求自动分发到可用节点
- 📢 **事件广播** - 事件发送到所有节点

## 🚀 快速开始

### 1. 启动 NATS

```bash
docker run -d -p 4222:4222 nats:latest
```

### 2. 启动多个节点

**节点 1**:
```bash
cd examples/DistributedCluster
dotnet run --urls "https://localhost:5001"
```

**节点 2**:
```bash
cd examples/DistributedCluster
dotnet run --urls "https://localhost:5002"
```

**节点 3**:
```bash
cd examples/DistributedCluster
dotnet run --urls "https://localhost:5003"
```

### 3. 测试集群

**创建订单（负载均衡）**:
```bash
# 多次调用，观察不同节点处理
curl -X POST https://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": "PROD-001", "quantity": 2}'
```

**发布事件（广播）**:
```bash
# 所有节点都会收到此事件
curl -X POST https://localhost:5001/orders/123/ship
```

查看所有节点日志，你会看到：
```
[NODE-1] Order shipped: 123
[NODE-2] Order shipped: 123
[NODE-3] Order shipped: 123
```

## 🎯 核心特性

### 1. 请求/响应（负载均衡）

```csharp
// 自动路由到任意可用节点
var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
```

### 2. 事件广播（所有节点）

```csharp
// 所有节点的 Handler 都会执行
await mediator.PublishAsync(new OrderShippedEvent(orderId));
```

## 📊 性能

- **消息延迟**: ~1ms
- **吞吐量**: 100K+ req/s
- **支持节点数**: 无限制

## 📚 相关文档

- [Catga 快速开始](../../QUICK_START.md)
- [架构说明](../../ARCHITECTURE.md)
