# Catga OrderSystem Example

完整的 CQRS + Event Sourcing 示例，演示 Catga 所有核心功能。

## 功能演示

| 功能 | 说明 |
|------|------|
| CQRS | 命令/查询分离 |
| Event Sourcing | 事件溯源 |
| Flow DSL | 工作流编排 (If/Switch/ForEach) |
| 多后端 | InMemory / Redis / NATS |
| 集群模式 | 多节点分布式部署 |
| Hosted Services | 自动恢复、生命周期管理、Outbox处理 |
| Health Checks | Kubernetes 就绪/存活探针 |
| AOT | Native AOT 编译支持 |
| MemoryPack | 高性能二进制序列化 |

## 快速开始

```bash
# 单机模式 (InMemory)
dotnet run

# Redis 后端
dotnet run -- --transport redis --persistence redis

# NATS 后端
dotnet run -- --transport nats --persistence nats

# 集群模式 (3节点)
dotnet run -- --cluster --port 5001 --transport redis --persistence redis
dotnet run -- --cluster --port 5002 --transport redis --persistence redis
dotnet run -- --cluster --port 5003 --transport redis --persistence redis
```

## 命令行参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--transport` | `inmemory` | 传输后端: inmemory/redis/nats |
| `--persistence` | `inmemory` | 持久化后端: inmemory/redis/nats |
| `--port` | `5000` | HTTP 端口 |
| `--cluster` | - | 启用集群模式 |
| `--node-id` | auto | 节点ID |

## API 端点

### 系统
- `GET /` - 系统信息
- `GET /health` - 健康检查
- `GET /stats` - 统计信息

### 订单管理
- `POST /orders` - 创建订单
- `GET /orders` - 订单列表
- `GET /orders/{id}` - 订单详情
- `POST /orders/{id}/pay` - 支付
- `POST /orders/{id}/ship` - 发货
- `POST /orders/{id}/cancel` - 取消
- `GET /orders/{id}/history` - 事件历史

### Flow DSL
- `POST /api/flows/fulfillment/start` - 启动履约流程
- `POST /api/flows/complex/start` - 启动复杂流程
- `GET /api/flows/status/{flowId}` - 流程状态
- `POST /api/flows/resume/{flowId}` - 恢复流程
- `POST /api/flows/cancel/{flowId}` - 取消流程

## 测试

```bash
# 运行所有测试
.\test.ps1

# 指定场景
.\test.ps1 -Scenario api      # API测试
.\test.ps1 -Scenario flow     # Flow DSL测试
.\test.ps1 -Scenario cluster  # 集群测试

# 跳过特定测试
.\test.ps1 -SkipRedis -SkipNats
```

## 项目结构

```
OrderSystem/
├── Commands/           # 命令定义
├── Queries/            # 查询定义
├── Events/             # 事件定义
├── Handlers/           # 命令/查询/事件处理器
├── Flows/              # Flow DSL 工作流
│   ├── OrderFulfillmentFlow.cs   # 订单履约流程
│   └── ComplexOrderFlow.cs       # 复杂流程 (Switch/ForEach)
├── Models/             # 领域模型
├── Extensions/         # 服务配置扩展
├── Program.cs          # 入口点
└── test.ps1            # 一键测试脚本
```

## Flow DSL 示例

### OrderFulfillmentFlow
演示: 顺序执行、条件分支、补偿(Saga)、事件发布

```csharp
flow.Send<CreateOrderCommand, OrderCreatedResult>(...)
    .Into((state, result) => state.OrderId = result.OrderId)
    .IfFail(state => new CancelOrderCommand(state.OrderId));

flow.If(state => state.Total > 0)
    .Send<GetOrderQuery, Order?>(...)
    .EndIf();

flow.Send(state => new PayOrderCommand(...))
    .IfFail(state => new CancelOrderCommand(...));
```

### ComplexOrderFlow
演示: Switch分支、ForEach迭代

```csharp
flow.Switch(state => state.Type)
    .Case(OrderType.Standard, branch => ...)
    .Case(OrderType.Express, branch => ...)
    .Default(branch => ...);

flow.ForEach(state => state.Items)
    .Configure((item, builder) => ...)
    .OnItemSuccess((state, item, result) => state.ProcessedItems++)
    .EndForEach();
```

## Docker

```bash
# 启动 Redis + NATS
docker-compose up -d

# 或单独启动
docker run -d -p 6379:6379 redis:alpine
docker run -d -p 4222:4222 nats:alpine -js
```
