# 🎯 Catga 框架简化建议

## 📊 当前状况分析

### 问题
框架功能越来越多，可能过于复杂：
- ✅ CQRS 核心
- ✅ NATS 传输
- ✅ Saga 事务
- ✅ Outbox/Inbox
- ✅ 服务发现（5种实现！）
- ✅ 流处理
- ✅ 配置中心（刚加的）
- ✅ 事件溯源（刚加的）

**这确实有点多了！**

---

## 💡 简化方案

### 方案 1: 保持简洁核心 ⭐⭐⭐（推荐）

**核心框架 (Catga)**:
```
Catga/
├── Messages/           # ICommand, IQuery, IEvent
├── Pipeline/           # Mediator, Behaviors
├── Results/            # Result<T>, Error
└── DI/                 # 扩展方法
```

**可选扩展**:
```
Catga.Nats              # NATS 传输
Catga.Redis             # Redis 存储
Catga.Resilience        # 熔断、重试、限流
Catga.Saga              # Saga 事务
Catga.Transit           # Outbox/Inbox
Catga.ServiceDiscovery  # 服务发现
Catga.Streaming         # 流处理
```

**删除/延后**:
- ❌ 配置中心（可以用 Microsoft.Extensions.Configuration）
- ❌ 事件溯源（太复杂，可以单独做项目）

**优点**:
- ✅ 核心简洁
- ✅ 按需引入
- ✅ 学习曲线平缓

---

### 方案 2: 分层架构 ⭐⭐

**核心层 (必需)**:
- Catga - CQRS 核心

**基础层 (常用)**:
- Catga.Nats - 分布式消息
- Catga.Redis - 状态存储

**增强层 (可选)**:
- Catga.Transit - Outbox/Inbox
- Catga.Saga - 分布式事务
- Catga.Resilience - 弹性设计

**高级层 (高级场景)**:
- Catga.ServiceDiscovery - 服务发现
- Catga.Streaming - 流处理
- Catga.EventSourcing - 事件溯源（新）

---

### 方案 3: 精简版 ⭐（极简）

**只保留最核心的**:
```
Catga               # CQRS 核心
Catga.Nats          # 分布式消息
Catga.Redis         # 状态存储
Catga.Transit       # Outbox/Inbox
```

**其他全部删除或移到单独仓库**:
- ServiceDiscovery → 独立项目
- Streaming → 独立项目
- Saga → 简化或删除

---

## 🎯 建议行动

### 立即执行

#### 1. 删除刚加的功能
```bash
# 删除配置中心
rm -rf src/Catga/Configuration/
rm -rf src/Catga/DependencyInjection/ConfigurationCenterExtensions.cs

# 删除事件溯源
rm -rf src/Catga/EventSourcing/
rm -rf src/Catga/DependencyInjection/EventSourcingExtensions.cs
rm -rf examples/EventSourcingDemo/
rm -rf docs/patterns/event-sourcing.md
```

#### 2. 简化服务发现

**问题**: 5 种实现太多了
- MemoryServiceDiscovery
- DnsServiceDiscovery
- ConsulServiceDiscovery
- YarpServiceDiscovery ← 可能不需要
- KubernetesServiceDiscovery

**建议**: 保留 3 种核心实现
- MemoryServiceDiscovery（开发/测试）
- DnsServiceDiscovery（K8s 基础）
- ConsulServiceDiscovery（企业级）

**删除**:
- YarpServiceDiscovery（YARP 本身就是服务发现）
- 或者把 ServiceDiscovery 单独成项目

#### 3. 简化流处理

**当前**: 10+ 个操作符，太多了

**建议**: 保留核心 5 个
- Where (过滤)
- Select (转换)
- Batch (批处理)
- Distinct (去重)
- Do (副作用)

**删除**:
- Window
- Throttle
- Delay
- Parallel (可以用 Task.WhenAll)
- DoAsync (合并到 Do)

---

## 📊 复杂度对比

### 当前复杂度 ⚠️

| 项目 | 文件数 | 复杂度 |
|-----|--------|--------|
| Catga | ~50 | 高 |
| Catga.Nats | ~10 | 中 |
| Catga.Redis | ~15 | 中 |
| Catga.ServiceDiscovery.* | ~15 | 高 |
| 示例项目 | ~8 | 高 |
| **总计** | **~100+** | **😵 过高** |

### 简化后复杂度 ✅

| 项目 | 文件数 | 复杂度 |
|-----|--------|--------|
| Catga | ~30 | 中 |
| Catga.Nats | ~10 | 中 |
| Catga.Redis | ~12 | 中 |
| Catga.Transit | ~8 | 低 |
| 示例项目 | ~4 | 低 |
| **总计** | **~60** | **😊 合理** |

---

## 🎊 最终建议

### 核心原则
1. **保持简单** - 只做最必要的
2. **渐进增强** - 高级功能可选
3. **关注核心** - CQRS + 分布式消息
4. **易于上手** - 30 分钟能理解

### 保留的核心功能
- ✅ CQRS (Command/Query/Event)
- ✅ Mediator + Pipeline
- ✅ NATS 传输
- ✅ Saga 事务
- ✅ Outbox/Inbox
- ✅ Redis 存储
- ✅ 基础弹性（熔断、重试）

### 删除/移除的功能
- ❌ 配置中心 → 用 Microsoft.Extensions.Configuration
- ❌ 事件溯源 → 太复杂，不属于 CQRS 框架核心
- ❌ 复杂的服务发现 → 简化到 2-3 种
- ❌ 过多的流操作符 → 简化到 5 个核心

### 未来可以考虑（独立项目）
- Catga.EventStore (事件溯源)
- Catga.Discovery (服务发现)
- Catga.Streaming (流处理)

---

## 🚀 执行计划

### Phase 1: 回滚最近的改动
```bash
# 删除配置中心和事件溯源
git reset --hard HEAD~2  # 回滚最近的提交

# 或者手动删除
rm -rf src/Catga/Configuration/
rm -rf src/Catga/EventSourcing/
rm -rf examples/EventSourcingDemo/
```

### Phase 2: 简化服务发现
- 删除 YarpServiceDiscovery
- 或者移到独立包 Catga.ServiceDiscovery.Yarp（可选）

### Phase 3: 简化流处理
- 只保留 5 个核心操作符
- 删除复杂示例

### Phase 4: 更新文档
- 强调核心功能
- 高级功能标记为"可选"
- 简化示例

---

## 💭 你的决定

**选项 A**: 执行完整简化（推荐）
- 删除配置中心和事件溯源
- 简化服务发现（只保留 3 种）
- 简化流处理（只保留 5 个操作符）

**选项 B**: 只删除最新的
- 删除配置中心和事件溯源
- 保留其他功能

**选项 C**: 全部保留
- 把新功能移到单独的包
- 标记为"实验性"

**选项 D**: 你有其他想法？

---

**建议**: 选择 **选项 A** 或 **选项 B**，保持框架简洁专注。

