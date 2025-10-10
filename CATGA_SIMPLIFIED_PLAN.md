# Catga - 简化计划

**日期**: 2025-10-10  
**核心目标**: 简单、AOT、高性能、集群、分布式、安全、稳定、节点互通、自动

---

## 🎯 核心定位

**Catga = 最简单的高性能 CQRS + 分布式框架**

### 特点
- ✅ **简单** - 3行代码启动，0配置
- ✅ **AOT** - 完全支持 Native AOT
- ✅ **高性能** - 100万+ QPS，0 GC
- ✅ **分布式** - 节点自动发现，自动互通
- ✅ **安全** - 分布式锁，幂等性
- ✅ **稳定** - 自动重试，故障转移

---

## 📦 核心包结构（精简）

```
Catga/
├── Catga                        # 核心（只有接口和抽象）
│   ├── IMessage, IRequest, IEvent
│   ├── IRequestHandler, IEventHandler
│   ├── ICatgaMediator
│   └── CatgaResult<T>
│
├── Catga.InMemory               # 内存实现（测试/开发）
│   ├── CatgaMediator
│   ├── InMemoryTransport
│   └── 所有内存实现
│
├── Catga.Transport.Nats         # NATS 传输（分布式核心）
│   ├── NatsTransport            # 消息传输
│   ├── NatsNodeDiscovery        # 节点发现
│   └── 自动节点互通
│
├── Catga.Persistence.Redis      # Redis 持久化
│   ├── RedisDistributedLock     # 分布式锁
│   ├── RedisDistributedCache    # 分布式缓存
│   └── RedisIdempotency         # 幂等性
│
├── Catga.SourceGenerator        # 源代码生成
│   └── 自动注册 Handler
│
└── Catga.Analyzers              # 代码分析器
    └── AOT、性能、安全检查
```

---

## 🚀 用户使用（极简）

### 1. 单机模式（开发/测试）

```csharp
// ✅ 2行代码
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 自动注册所有 Handler
```

### 2. 分布式模式（生产）

```csharp
// ✅ 3行代码
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();
builder.Services.AddNatsTransport("nats://localhost:4222");

// ✅ 节点自动发现
// ✅ 消息自动路由
// ✅ 故障自动转移
```

### 3. 使用（完全不变）

```csharp
// ✅ 定义消息
public record CreateOrderCommand(string ProductId, int Quantity) 
    : IRequest<Order>;

// ✅ 定义 Handler
public class CreateOrderHandler 
    : IRequestHandler<CreateOrderCommand, Order>
{
    public async Task<CatgaResult<Order>> HandleAsync(
        CreateOrderCommand request, 
        CancellationToken ct)
    {
        var order = new Order(...);
        await _repository.SaveAsync(order, ct);
        return CatgaResult<Order>.Success(order);
    }
}

// ✅ 使用
var result = await _mediator.SendAsync<CreateOrderCommand, Order>(command);

// ✅ 单机：本地处理
// ✅ 分布式：自动路由到可用节点
```

---

## 🌟 核心功能（必须实现）

### 1. 节点自动发现（NATS）
- ✅ 节点启动自动加入
- ✅ 节点下线自动移除
- ✅ 心跳检测（30秒）
- ✅ 节点元数据（IP、负载等）

### 2. 消息自动路由
- ✅ 轮询（默认）
- ✅ 一致性哈希（相同Key到同一节点）
- ✅ 本地优先（先本地，失败再远程）
- ✅ 广播（发送到所有节点）

### 3. 故障自动转移
- ✅ 自动重试（3次）
- ✅ 自动切换节点
- ✅ 断路器（防雪崩）

### 4. 分布式锁（Redis）
- ✅ 简单易用
- ✅ 自动释放
- ✅ 死锁检测

### 5. 幂等性（Redis）
- ✅ 自动去重
- ✅ 基于MessageId
- ✅ TTL自动过期

---

## 💡 分布式实现（核心）

### 方案：NATS JetStream

**为什么选择NATS**：
- ✅ 极简（1个二进制）
- ✅ 高性能（百万级QPS）
- ✅ 自带服务发现
- ✅ 自带消息路由
- ✅ 国内可用

**实现**：
```csharp
// Catga.Transport.Nats/NatsTransport.cs

public class NatsTransport : IMessageTransport
{
    private readonly INatsConnection _nats;
    
    // ✅ 发布消息（自动路由）
    public async Task PublishAsync<T>(T message, ...)
    {
        var subject = $"catga.{typeof(T).Name}";
        await _nats.PublishAsync(subject, message);
    }
    
    // ✅ 订阅消息（自动接收）
    public async Task SubscribeAsync<T>(...)
    {
        var subject = $"catga.{typeof(T).Name}";
        await _nats.SubscribeAsync<T>(subject, handler);
    }
}

// Catga.Transport.Nats/NatsNodeDiscovery.cs

public class NatsNodeDiscovery
{
    // ✅ 节点注册（启动时）
    public async Task RegisterAsync()
    {
        var node = new { NodeId, IP, Load };
        await _nats.PublishAsync("catga.nodes.join", node);
    }
    
    // ✅ 心跳（每30秒）
    public async Task HeartbeatAsync()
    {
        var heartbeat = new { NodeId, Load };
        await _nats.PublishAsync("catga.nodes.heartbeat", heartbeat);
    }
    
    // ✅ 获取所有节点
    public async Task<List<Node>> GetNodesAsync()
    {
        // 从 KV Store 读取
        return await _nats.GetAllAsync<Node>("catga.nodes");
    }
}
```

---

## 📊 性能目标

| 指标 | 目标 | 说明 |
|------|------|------|
| 吞吐量 | 100万+ QPS | 单机 |
| 延迟 | < 1ms | 本地处理 P99 |
| 延迟 | < 5ms | 跨节点 P99 |
| GC | 0 分配 | 关键路径 |
| 启动 | < 100ms | 节点加入 |
| 内存 | < 50MB | 空闲状态 |

---

## ⏱️ 实现计划

### Phase 1: 核心清理（1天）
- [x] 删除 Catga.Cluster
- [ ] 修复编译错误
- [ ] 清理无用代码
- [ ] 更新文档

### Phase 2: NATS 集成（2天）
- [ ] NatsTransport（消息传输）
- [ ] NatsNodeDiscovery（节点发现）
- [ ] 自动心跳
- [ ] 自动故障转移

### Phase 3: 消息路由（1天）
- [ ] 轮询路由
- [ ] 一致性哈希
- [ ] 本地优先
- [ ] 广播

### Phase 4: 示例和文档（1天）
- [ ] 简单示例（单机）
- [ ] 分布式示例（3节点）
- [ ] Docker Compose
- [ ] 完整文档

### Phase 5: 性能优化（1天）
- [ ] AOT 警告清理
- [ ] 性能测试
- [ ] 基准测试
- [ ] 文档完善

**总计：6天**

---

## 🎯 核心理念

### 1. 极简
- ❌ 不引入复杂概念
- ❌ 不需要手动配置
- ✅ 3行代码启动
- ✅ 代码完全不变

### 2. 高性能
- ✅ 0 GC（Span/Memory）
- ✅ 对象池（ArrayPool）
- ✅ 无锁设计
- ✅ AOT 优化

### 3. 分布式
- ✅ 用成熟组件（NATS）
- ✅ 自动节点发现
- ✅ 自动故障转移
- ✅ 无需手动配置

---

## 🚀 Docker Compose 示例

```yaml
version: '3.8'
services:
  nats:
    image: nats:latest
    ports:
      - "4222:4222"
    command: ["-js"]  # JetStream

  node1:
    image: myapp:latest
    environment:
      - NATS_URL=nats://nats:4222
    ports:
      - "5001:80"

  node2:
    image: myapp:latest
    environment:
      - NATS_URL=nats://nats:4222
    ports:
      - "5002:80"

  node3:
    image: myapp:latest
    environment:
      - NATS_URL=nats://nats:4222
    ports:
      - "5003:80"
```

启动：
```bash
docker-compose up -d
# ✅ 3节点自动发现
# ✅ 消息自动路由
# ✅ 故障自动转移
```

---

## 🎉 总结

**Catga = 最简单的高性能分布式 CQRS 框架**

### 核心价值
- ✅ **超简单** - 3行代码
- ✅ **高性能** - 100万+ QPS, 0 GC
- ✅ **AOT 支持** - 完全兼容
- ✅ **分布式** - NATS自动化
- ✅ **安全稳定** - 锁、幂等、重试

### 对比其他框架

| 特性 | Catga | MediatR | Mass Transit | Orleans |
|------|-------|---------|--------------|---------|
| 简单 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| 性能 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ |
| AOT | ⭐⭐⭐⭐⭐ | ❌ | ❌ | ⭐⭐ |
| 分布式 | ⭐⭐⭐⭐⭐ | ❌ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| 配置 | 3行 | 2行 | 20+行 | 10+行 |

---

**🚀 Catga v3.0 - 简单、快速、分布式！**

*Let's build it!* 🎊

