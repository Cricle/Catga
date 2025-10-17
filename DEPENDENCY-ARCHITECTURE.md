# 🏗️ Catga 依赖架构

## ✅ 修复完成：序列化库依赖清理

### 问题
序列化库（`Catga.Serialization.Json` 和 `Catga.Serialization.MemoryPack`）错误地依赖了 `Catga.InMemory`，导致：
- ❌ 循环依赖风险
- ❌ 序列化库不能独立使用
- ❌ 架构层次混乱

### 解决方案
将 `CatgaServiceBuilder` 从 `Catga.InMemory` 移动到 `Catga` 核心库。

---

## 📊 当前依赖层次（正确）

```
┌──────────────────────────────────────────┐
│          Application Layer               │
│    (OrderSystem, Examples, Tests)        │
└──────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────┐
│      Infrastructure Layer (可选)          │
│  Catga.InMemory, Catga.Transport.Nats,  │
│  Catga.Persistence.Redis                 │
└──────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────┐
│       Serialization Layer (可选)          │
│  Catga.Serialization.Json                │
│  Catga.Serialization.MemoryPack          │
└──────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────┐
│          Core Library (必需)              │
│              Catga                        │
│  (包含 CatgaServiceBuilder)               │
└──────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────┐
│       Source Generators (编译时)          │
│         Catga.SourceGenerator            │
└──────────────────────────────────────────┘
```

---

## 🎯 各层职责

### 1. Core Library (`Catga`)
**职责**: 核心抽象和基础设施
- ✅ CQRS/Event Sourcing 抽象
- ✅ 消息接口 (`IMessage`, `IEvent`, `IRequest`)
- ✅ 序列化抽象 (`IMessageSerializer`, `IBufferedMessageSerializer`)
- ✅ 配置和构建器 (`CatgaOptions`, `CatgaServiceBuilder`)
- ✅ 性能工具 (`ArrayPoolHelper`, `SnowflakeIdGenerator`)
- ✅ AOT 兼容性

**依赖**:
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `System.Diagnostics.DiagnosticSource` (for OpenTelemetry)

---

### 2. Serialization Layer (可选)

#### `Catga.Serialization.Json`
**职责**: JSON 序列化实现
- ✅ `JsonMessageSerializer`
- ✅ `IBufferedMessageSerializer` 实现（零拷贝）
- ✅ AOT 兼容（需要 `JsonSerializerContext`）

**依赖**: ✅ 仅 `Catga`

#### `Catga.Serialization.MemoryPack`
**职责**: MemoryPack 序列化实现
- ✅ `MemoryPackMessageSerializer`
- ✅ `IBufferedMessageSerializer` 实现（零拷贝）
- ✅ 100% AOT 兼容

**依赖**: ✅ 仅 `Catga` + `MemoryPack` NuGet

---

### 3. Infrastructure Layer (可选)

#### `Catga.InMemory`
**职责**: 内存实现（开发/测试）
- ✅ `InMemoryEventStore`
- ✅ `InMemoryMediator`
- ✅ `CatgaServiceCollectionExtensions` (AddCatga)

**依赖**: ✅ `Catga` + 序列化库（可选）

#### `Catga.Transport.Nats`
**职责**: NATS 消息传输
- ✅ NATS JetStream 集成
- ✅ `NatsEventStore`

**依赖**: ✅ `Catga` + `NATS.Client.Core`

#### `Catga.Persistence.Redis`
**职责**: Redis 持久化
- ✅ Redis 缓存
- ✅ Redis Outbox Store（优化）
- ✅ Span<T> 零拷贝优化

**依赖**: ✅ `Catga` + `StackExchange.Redis`

---

### 4. Source Generators (编译时)

#### `Catga.SourceGenerator`
**职责**: 编译时代码生成
- ✅ DI 注册生成
- ✅ 事件路由生成
- ✅ AOT 优化

**依赖**: Roslyn API

---

## 📋 依赖验证清单

### ✅ Catga (Core)
```bash
dotnet list src/Catga/Catga.csproj reference
# 输出：无项目引用（仅 NuGet 包）
```

### ✅ Catga.Serialization.Json
```bash
dotnet list src/Catga.Serialization.Json/Catga.Serialization.Json.csproj reference
# 输出：
# ..\Catga\Catga.csproj
```

### ✅ Catga.Serialization.MemoryPack
```bash
dotnet list src/Catga.Serialization.MemoryPack/Catga.Serialization.MemoryPack.csproj reference
# 输出：
# ..\Catga\Catga.csproj
```

### ✅ Catga.InMemory
```bash
dotnet list src/Catga.InMemory/Catga.InMemory.csproj reference
# 输出：
# ..\Catga\Catga.csproj
# ..\Catga.Serialization.Json\Catga.Serialization.Json.csproj (可选)
```

---

## 🎯 使用场景

### Scenario 1: 最小依赖（仅核心 + JSON）
```xml
<ItemGroup>
  <ProjectReference Include="Catga/Catga.csproj" />
  <ProjectReference Include="Catga.Serialization.Json/Catga.Serialization.Json.csproj" />
</ItemGroup>
```

**用途**: 
- 自定义基础设施
- 不需要内存实现
- 最小化依赖

---

### Scenario 2: 快速开发（内存 + JSON）
```xml
<ItemGroup>
  <ProjectReference Include="Catga/Catga.csproj" />
  <ProjectReference Include="Catga.Serialization.Json/Catga.Serialization.Json.csproj" />
  <ProjectReference Include="Catga.InMemory/Catga.InMemory.csproj" />
</ItemGroup>
```

**用途**:
- 快速原型
- 开发/测试环境
- 无需外部依赖

---

### Scenario 3: 生产 AOT（MemoryPack + NATS + Redis）
```xml
<ItemGroup>
  <ProjectReference Include="Catga/Catga.csproj" />
  <ProjectReference Include="Catga.Serialization.MemoryPack/Catga.Serialization.MemoryPack.csproj" />
  <ProjectReference Include="Catga.Transport.Nats/Catga.Transport.Nats.csproj" />
  <ProjectReference Include="Catga.Persistence.Redis/Catga.Persistence.Redis.csproj" />
</ItemGroup>
```

**用途**:
- 生产环境
- 100% AOT 兼容
- 高性能分布式系统

---

## 🚫 反模式（避免）

### ❌ 错误 1: 序列化库依赖 InMemory
```xml
<!-- ❌ 错误 -->
<ItemGroup>
  <ProjectReference Include="..\Catga.InMemory\Catga.InMemory.csproj" />
</ItemGroup>
```

**问题**: 序列化库应该独立，不应该依赖具体实现。

---

### ❌ 错误 2: Core 依赖具体实现
```xml
<!-- ❌ 错误 -->
<ItemGroup>
  <ProjectReference Include="..\Catga.Transport.Nats\Catga.Transport.Nats.csproj" />
</ItemGroup>
```

**问题**: Core 应该只包含抽象，不应该依赖具体实现。

---

### ❌ 错误 3: 循环依赖
```
Catga.InMemory → Catga.Serialization.Json → Catga.InMemory
```

**问题**: 循环依赖导致编译失败或架构混乱。

---

## 📝 修复历史

### 2025-10-17: 序列化库依赖清理 ✅

**变更**:
1. 移动 `CatgaServiceBuilder` 从 `Catga.InMemory` 到 `Catga`
2. 移除 `Catga.Serialization.MemoryPack` 对 `Catga.InMemory` 的引用
3. 修改 `CatgaServiceBuilder` 构造函数为 `public`

**提交**: `ddbf9bf refactor: Move CatgaServiceBuilder to Catga core library`

**验证**:
- ✅ 编译成功：0 警告，0 错误
- ✅ 测试通过：194/194 个单元测试
- ✅ 依赖清单：所有序列化库仅依赖 `Catga`

---

## 🎉 收益

### 架构清晰
- ✅ 单向依赖流：Application → Infrastructure → Serialization → Core
- ✅ 无循环依赖
- ✅ 每层职责明确

### 灵活性
- ✅ 序列化库可独立使用
- ✅ 可选择性引用基础设施
- ✅ 支持自定义实现

### 可维护性
- ✅ 依赖变更影响最小化
- ✅ 易于理解和扩展
- ✅ 符合依赖倒置原则（DIP）

---

## 🔗 相关文档
- [SPAN-OPTIMIZATION-PLAN.md](./SPAN-OPTIMIZATION-PLAN.md) - Span<T> 零拷贝优化
- [ARRAYPOOL-OPTIMIZATION-PLAN.md](./ARRAYPOOL-OPTIMIZATION-PLAN.md) - ArrayPool 内存优化
- [MULTI-TARGETING-COMPLETE.md](./MULTI-TARGETING-COMPLETE.md) - 多目标框架支持

🎯 **清晰的架构 = 可维护的代码！**

