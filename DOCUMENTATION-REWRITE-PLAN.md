# 📚 Catga 文档重写计划

> **目标**: 创建清晰、完整、用户友好的文档体系，反映最新的架构优化和 UX 改进

**执行日期**: 2025-10-14
**优先级**: P0 (发布前必做)

---

## 🎯 核心问题分析

### 当前文档问题

1. **架构描述过时**
   - ❌ README 中仍引用已删除的 `Catga.Distributed.Nats/Redis`
   - ❌ 节点发现描述不准确（已移除应用层实现）
   - ❌ 序列化器配置说明过时（未体现新的 Fluent API）

2. **配置示例过时**
   - ❌ 使用旧的配置方式：`services.AddCatga(options => ...)`
   - ❌ 未展示新的 Builder API：`services.AddCatga().UseMemoryPack().ForProduction()`
   - ❌ 缺少分析器和启动验证的说明

3. **快速入门不够简洁**
   - ❌ QUICK-REFERENCE.md 15 行配置，实际只需 3 行
   - ❌ 缺少 "最简示例" - 30 秒上手
   - ❌ 序列化器注册说明不清晰

4. **架构图不准确**
   - ❌ 层次结构图包含已删除的组件
   - ❌ 缺少最新的职责边界说明
   - ❌ 未体现 K8s/编排平台的定位

5. **AOT 指南分散**
   - ❌ MemoryPack AOT 说明分散在多个文件
   - ❌ 缺少 "MemoryPack vs JSON" 决策指南
   - ❌ 未强调 MemoryPack 的 AOT 优势

6. **示例项目不完整**
   - ❌ OrderSystem 项目需要更新配置
   - ❌ 缺少 MemoryPack 示例
   - ❌ 缺少分析器演示

---

## 📋 重写计划

### Phase 1: 核心文档更新 (P0 - 必做)

#### 1.1 README.md 重写

**目标**: 5 分钟快速了解 Catga，30 秒开始使用

**关键改进**:
```markdown
## ✅ Before (问题)
- 15 行配置代码
- 5 个 using 语句
- 复杂的序列化器注册
- 节点发现配置（已移除）

## ✅ After (改进)
- 3 行配置代码
- 1 个 using 语句
- .UseMemoryPack() 一键配置
- 清晰的架构边界
```

**章节结构**:
```markdown
# Catga

## 🎯 30 秒快速开始
- 最简示例（真的只需 3 行）
- 立即可运行的代码

## ✨ 核心特性
- **100% AOT 兼容** - MemoryPack 零反射
- **极简 API** - Fluent Builder 风格
- **编译时检查** - Roslyn 分析器
- **生产就绪** - 完整的可观测性

## 🚀 5 分钟教程
- 安装
- 定义消息（[MemoryPackable]）
- 实现 Handler
- 配置服务（3 行）
- 运行

## 🏗️ 架构
- 清晰的层次结构图
- 职责边界说明
- K8s/编排平台定位

## 📚 完整文档
- 分类清晰的文档索引
```

**文件**: `README.md`
**预计时间**: 2 小时
**优先级**: P0

---

#### 1.2 QUICK-REFERENCE.md 重写

**目标**: 真正的 "5 分钟快速参考"

**关键改进**:
```markdown
## ✅ 最简配置（新增）
```csharp
// 仅需 3 行！
services.AddCatga()
    .UseMemoryPack()      // 100% AOT
    .ForProduction();
```

## ✅ 消息定义（强调 MemoryPack）
```csharp
[MemoryPackable]  // ← AOT 必需
public partial record CreateOrder(...) : IRequest<OrderResult>;
```

## ✅ 环境预设（新增）
- ForDevelopment() - 开发环境
- ForProduction() - 生产环境
- ForHighPerformance() - 高性能场景
- Minimal() - 最小化

## ✅ 分析器提示（新增）
- CATGA001: 缺少 [MemoryPackable]
- CATGA002: 缺少序列化器注册
```

**文件**: `QUICK-REFERENCE.md`
**预计时间**: 1.5 小时
**优先级**: P0

---

#### 1.3 docs/README.md 重写

**目标**: 清晰的文档导航中心

**关键改进**:
```markdown
## 🚀 新手路径（3 步上手）
1. [30 秒快速开始](../README.md#-30秒快速开始)
2. [配置序列化器](guides/serialization.md) ← 新增
3. [部署到生产](deployment/production.md)

## 🎓 进阶路径（5 步精通）
1. [理解架构](architecture/ARCHITECTURE.md)
2. [使用分析器](guides/analyzers.md)
3. [性能优化](guides/performance.md)
4. [分布式部署](distributed/README.md)
5. [可观测性](guides/observability.md)

## 🏗️ 架构决策记录（新增）
- [为什么移除应用层节点发现](adr/001-remove-app-discovery.md)
- [为什么选择 MemoryPack](adr/002-memorypack-aot.md)
- [为什么分析器优于运行时检查](adr/003-analyzers.md)
```

**文件**: `docs/README.md`
**预计时间**: 1 小时
**优先级**: P0

---

### Phase 2: 架构文档更新 (P0 - 必做)

#### 2.1 docs/architecture/ARCHITECTURE.md

**目标**: 准确反映当前架构

**关键更新**:
```markdown
## 🏗️ 当前架构（2025-10）

### 核心层次
```
┌─────────────────────────────────────┐
│        Your Application             │ ← 业务逻辑
├─────────────────────────────────────┤
│   Catga.Serialization.MemoryPack    │ ← 序列化（推荐）
│   Catga.Serialization.Json          │ ← 或 JSON
├─────────────────────────────────────┤
│      Catga.InMemory (Core)          │ ← 核心实现
├─────────────────────────────────────┤
│         Catga (Abstractions)        │ ← 接口定义
├─────────────────────────────────────┤
│    Catga.SourceGenerator            │ ← 编译时代码生成
└─────────────────────────────────────┘

### 可选扩展（基础设施无关）
┌──────────────────┬─────────────────┐
│  Transport Layer │ Persistence     │
│  - Catga.Trans-  │ - Catga.Persis- │
│    port.Nats     │   tence.Redis   │
└──────────────────┴─────────────────┘

### 编排层（外部）
┌─────────────────────────────────────┐
│  Kubernetes / .NET Aspire           │ ← 节点发现
│  - Service Discovery                │   分布式协调
│  - Load Balancing                   │   服务网格
│  - Health Checks                    │
└─────────────────────────────────────┘
```

### 职责边界

**Catga 负责**:
- ✅ CQRS 消息分发
- ✅ Pipeline 管道
- ✅ 幂等性保证
- ✅ 消息序列化接口
- ✅ 可观测性（Metrics/Tracing/Logging）

**Catga 不负责**:
- ❌ 节点发现（交给 K8s/Aspire）
- ❌ 负载均衡（交给 K8s Service）
- ❌ 服务网格（交给 Istio/Linkerd）
- ❌ 消息队列实现（使用 NATS/Redis 原生能力）

### 设计原则

1. **基础设施无关**
   - Transport/Persistence 层不依赖序列化器
   - 用户显式选择序列化方式

2. **编排平台优先**
   - 依赖成熟的编排平台
   - 不重复实现已有能力

3. **AOT 优先**
   - 所有代码 AOT 兼容
   - 推荐 MemoryPack（100% AOT）
```

**文件**: `docs/architecture/ARCHITECTURE.md`
**预计时间**: 2 小时
**优先级**: P0

---

#### 2.2 docs/architecture/RESPONSIBILITY-BOUNDARY.md

**目标**: 清晰的职责边界文档

**内容**:
```markdown
# 职责边界 - Catga vs NATS/Redis vs K8s

## 概述

Catga 的核心职责是 **CQRS 消息分发和处理**，而非重新实现消息队列或编排平台。

## 三层架构

### Layer 1: Catga 核心
**职责**: CQRS 模式、Pipeline、幂等性
**不负责**: 消息传输实现、节点发现

### Layer 2: 消息中间件
**NATS 职责**: Pub/Sub、Request/Reply、Stream、JetStream
**Redis 职责**: Streams、Pub/Sub、持久化

**Catga 的做法**: 直接使用原生能力，不重复实现

### Layer 3: 编排平台
**K8s 职责**: Service Discovery、Load Balancing、Health Checks
**Aspire 职责**: 本地开发编排、服务发现

**Catga 的做法**: 完全依赖，不自己实现

## 决策理由

### ❌ 为什么移除应用层节点发现？

**Before**:
```csharp
services.AddNatsNodeDiscovery();  // 自己实现
services.AddRedisNodeDiscovery(); // 重复造轮子
```

**After**:
```yaml
# 使用 K8s Service Discovery
apiVersion: v1
kind: Service
metadata:
  name: order-service
```

**理由**:
1. K8s 已经完美解决
2. 应用层实现不如平台层
3. 减少代码复杂度
4. 更好的跨平台支持

### ✅ 为什么保留 QoS？

**Catga 保留**:
- AtMostOnce / AtLeastOnce / ExactlyOnce
- 幂等性保证
- 重试逻辑

**理由**: 这是 CQRS 模式的一部分，不是基础设施
```

**文件**: `docs/architecture/RESPONSIBILITY-BOUNDARY.md`
**预计时间**: 1.5 小时
**优先级**: P0

---

### Phase 3: 序列化指南 (P0 - 必做)

#### 3.1 docs/guides/serialization.md (新建)

**目标**: 一站式序列化指南

**内容**:
```markdown
# 序列化指南

## 快速决策

```mermaid
graph TD
    A[需要 AOT?] -->|是| B[MemoryPack]
    A -->|否| C[需要人类可读?]
    C -->|是| D[JSON]
    C -->|否| B

    B --> E[所有消息标注 [MemoryPackable]]
    D --> F[配置 JsonSerializerContext]
```

## MemoryPack (推荐)

### ✅ 优势
- 100% AOT 兼容
- 5x 性能提升
- 40% 更小 payload
- 零反射

### 📦 安装
```bash
dotnet add package Catga.Serialization.MemoryPack
dotnet add package MemoryPack
dotnet add package MemoryPack.Generator
```

### 🎯 使用
```csharp
// 1. 标注消息
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;

// 2. 配置（一行）
services.AddCatga().UseMemoryPack();

// 3. Done!
```

### ⚠️ 注意事项
- 必须标注 `[MemoryPackable]`
- 必须使用 `partial` 关键字
- 分析器会提示缺失

## JSON

### ⚠️ 限制
- 不推荐用于 AOT
- 需要配置 JsonSerializerContext
- 性能较低

### 📦 安装
```bash
dotnet add package Catga.Serialization.Json
```

### 🎯 AOT 使用
```csharp
// 1. 定义 Context
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(OrderResult))]
public partial class AppJsonContext : JsonSerializerContext { }

// 2. 配置
services.AddCatga().UseJson(new JsonSerializerOptions
{
    TypeInfoResolver = AppJsonContext.Default
});
```

## 性能对比

| 操作 | MemoryPack | JSON | 提升 |
|------|-----------|------|------|
| 序列化 | 50 ns | 250 ns | 5x |
| 反序列化 | 40 ns | 200 ns | 5x |
| Payload 大小 | 60% | 100% | 40% ↓ |
| AOT 兼容 | ✅ 100% | ⚠️ 需配置 | - |
```

**文件**: `docs/guides/serialization.md`
**预计时间**: 2 小时
**优先级**: P0

---

### Phase 4: 分析器文档 (P1 - 重要)

#### 4.1 docs/guides/analyzers.md 更新

**目标**: 完整的分析器使用指南

**新增内容**:
```markdown
# Catga 分析器完整指南

## 🆕 新增分析器 (v2.0)

### CATGA001: 缺少 [MemoryPackable] 属性

**严重性**: Info
**触发条件**: 消息类型实现 `IRequest` 或 `IEvent`，但未标注 `[MemoryPackable]`

**示例**:
```csharp
// ❌ 触发 CATGA001
public record CreateOrder(...) : IRequest<OrderResult>;

// ✅ 正确
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;
```

**自动修复**: 添加 `[MemoryPackable]` 属性

---

### CATGA002: 缺少序列化器注册

**严重性**: Warning
**触发条件**: 调用 `AddCatga()` 但未链式调用序列化器配置

**示例**:
```csharp
// ❌ 触发 CATGA002
services.AddCatga();

// ✅ 正确
services.AddCatga().UseMemoryPack();
```

**自动修复**: 添加 `.UseMemoryPack()` 调用

---

## 📋 完整规则列表

| ID | 规则 | 严重性 | 自动修复 |
|----|------|--------|---------|
| CATGA001 | 缺少 [MemoryPackable] | Info | ✅ |
| CATGA002 | 缺少序列化器注册 | Warning | ✅ |
| CATGA1001 | Handler 未实现接口 | Error | ❌ |
| ... | ... | ... | ... |
```

**文件**: `docs/guides/analyzers.md`
**预计时间**: 1.5 小时
**优先级**: P1

---

### Phase 5: 示例项目更新 (P1 - 重要)

#### 5.1 examples/OrderSystem 更新

**关键更新**:
1. **配置简化**
   ```csharp
   // Before (15 lines)
   services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
   services.AddCatga(options => {
       options.EnableLogging = true;
       options.EnableTracing = true;
       ...
   });

   // After (3 lines)
   services.AddCatga()
       .UseMemoryPack()
       .ForProduction();
   ```

2. **消息定义**
   ```csharp
   // 所有消息添加 [MemoryPackable]
   [MemoryPackable]
   public partial record CreateOrder(...) : IRequest<OrderResult>;
   ```

3. **README 更新**
   - 强调 3 行配置
   - 说明分析器提示
   - 展示性能数据

**文件**: `examples/OrderSystem/*`
**预计时间**: 2 小时
**优先级**: P1

---

#### 5.2 新增示例: MemoryPackAotDemo

**目标**: 展示 100% AOT 兼容的示例

**内容**:
```
examples/MemoryPackAotDemo/
├── Program.cs
├── Messages.cs             # [MemoryPackable] messages
├── Handlers.cs
├── MemoryPackAotDemo.csproj  # PublishAot=true
└── README.md
```

**README.md**:
```markdown
# MemoryPack AOT Demo

## ✨ 特性
- ✅ 100% AOT 兼容
- ✅ 零反射
- ✅ 50ms 启动时间
- ✅ 8MB 二进制

## 🚀 运行
```bash
# 开发
dotnet run

# AOT 发布
dotnet publish -c Release -r linux-x64 --property:PublishAot=true

# 运行
./bin/Release/net9.0/linux-x64/publish/MemoryPackAotDemo
```

## 📊 性能
- 启动时间: 50ms
- 内存占用: 12MB
- 二进制大小: 8MB
```

**文件**: `examples/MemoryPackAotDemo/*`
**预计时间**: 3 小时
**优先级**: P1

---

### Phase 6: 部署文档 (P2 - 可选)

#### 6.1 docs/deployment/kubernetes.md (新建)

**目标**: K8s 部署最佳实践

**内容**:
```markdown
# Kubernetes 部署指南

## 为什么选择 K8s？

Catga 设计时考虑了 K8s 的特性：
- ✅ 服务发现 → K8s Service
- ✅ 负载均衡 → K8s Service
- ✅ 健康检查 → K8s Probes
- ✅ 配置管理 → K8s ConfigMap

## 部署架构

```yaml
# order-service.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: order-service
        image: myregistry/order-service:latest
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: NATS__Url
          value: nats://nats:4222
        ports:
        - containerPort: 8080
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
---
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
  ports:
  - port: 80
    targetPort: 8080
```

## 服务发现

```csharp
// Catga 不需要特殊配置
// K8s 会自动处理服务发现

services.AddCatga()
    .UseMemoryPack()
    .UseNatsTransport(options =>
    {
        // 使用 K8s Service 名称
        options.Url = "nats://nats:4222";
    });
```

## 最佳实践

1. **使用 Native AOT**
   - 更快启动
   - 更小镜像
   - 更低内存

2. **配置健康检查**
   ```csharp
   app.MapHealthChecks("/health/live");
   app.MapHealthChecks("/health/ready");
   ```

3. **使用 HorizontalPodAutoscaler**
   ```yaml
   apiVersion: autoscaling/v2
   kind: HorizontalPodAutoscaler
   metadata:
     name: order-service-hpa
   spec:
     scaleTargetRef:
       apiVersion: apps/v1
       kind: Deployment
       name: order-service
     minReplicas: 2
     maxReplicas: 10
     metrics:
     - type: Resource
       resource:
         name: cpu
         target:
           type: Utilization
           averageUtilization: 70
   ```
```

**文件**: `docs/deployment/kubernetes.md`
**预计时间**: 2.5 小时
**优先级**: P2

---

## 📊 执行计划总结

### 时间估算

| Phase | 内容 | 时间 | 优先级 |
|-------|------|------|--------|
| Phase 1 | 核心文档更新 | 4.5h | P0 |
| Phase 2 | 架构文档 | 3.5h | P0 |
| Phase 3 | 序列化指南 | 2h | P0 |
| Phase 4 | 分析器文档 | 1.5h | P1 |
| Phase 5 | 示例项目 | 5h | P1 |
| Phase 6 | 部署文档 | 2.5h | P2 |
| **总计** | | **19h** | |

### 优先级说明

- **P0 (10h)** - 发布前必做，核心文档
- **P1 (6.5h)** - 发布后 1 周内完成
- **P2 (2.5h)** - 发布后 1 个月内完成

---

## 🎯 执行步骤

### Step 1: 立即开始 P0 任务 ✅
1. ✅ 更新 README.md (2h)
2. ✅ 更新 QUICK-REFERENCE.md (1.5h)
3. ✅ 更新 docs/README.md (1h)
4. ✅ 更新 ARCHITECTURE.md (2h)
5. ✅ 创建 RESPONSIBILITY-BOUNDARY.md (1.5h)
6. ✅ 创建 serialization.md (2h)

**预计完成时间**: 今天 (10h)

### Step 2: P1 任务 (明天)
1. 更新 analyzers.md (1.5h)
2. 更新 OrderSystem 示例 (2h)
3. 创建 MemoryPackAotDemo (3h)

**预计完成时间**: 明天 (6.5h)

### Step 3: P2 任务 (下周)
1. 创建 kubernetes.md (2.5h)

**预计完成时间**: 下周

---

## ✅ 验收标准

### 文档质量
- [ ] 所有代码示例可运行
- [ ] 所有配置示例正确
- [ ] 架构图准确
- [ ] 无过时信息

### 用户体验
- [ ] 新手 30 秒可开始
- [ ] 5 分钟完成第一个应用
- [ ] 清晰的决策指南（如序列化器选择）
- [ ] 完整的 API 参考

### 技术准确性
- [ ] 反映最新架构（移除节点发现）
- [ ] 正确的序列化器配置
- [ ] 准确的职责边界
- [ ] 完整的分析器说明

---

## 📝 文档写作规范

### 格式规范
1. **Markdown 标准**
   - 使用 GitHub Flavored Markdown
   - 代码块指定语言
   - 表格对齐

2. **代码示例**
   - 完整可运行
   - 包含必要的 using
   - 添加注释

3. **中英文混排**
   - 专有名词使用英文（如 CQRS、AOT、K8s）
   - 技术术语首次出现给出英文
   - 中英文之间加空格

### 内容规范
1. **开头 30 秒**
   - 明确目标受众
   - 说明阅读时长
   - 提供导航链接

2. **结构化**
   - 使用标题层级
   - emoji 辅助识别
   - 视觉分隔明确

3. **示例优先**
   - 先给示例，再解释
   - Before/After 对比
   - 标注好坏

---

## 🚀 现在开始执行

立即执行 Phase 1 - 核心文档更新 (P0)！

