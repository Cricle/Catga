# Catga 依赖清理总结

## 完成时间
2024-12-24

## 问题描述

Catga 核心库之前依赖了不应该在核心库中的具体实现：
1. **MemoryPack** - 序列化库，应该只在 `Catga.Serialization.MemoryPack` 中使用
2. **DistributedLock.WaitHandles** - 具体的锁实现，应该只在实现库中使用

这违反了分层架构原则，导致核心库过度依赖具体实现。

## 解决方案

### 1. 移除 MemoryPack 依赖

**修改的文件：**
- `src/Catga/Catga.csproj` - 移除 MemoryPack 包引用
- `src/Catga/Flow/FlowPosition.cs` - 移除 `[MemoryPackable]` 特性
- `src/Catga/Flow/StoredSnapshot.cs` - 移除 `[MemoryPackable]` 特性
- `src/Catga/Flow/WaitCondition.cs` - 移除 `[MemoryPackable]` 特性
- `src/Catga.Persistence.Nats/Stores/NatsSnapshotStore.cs` - 移除内部类的 `[MemoryPackable]` 特性

**设计原则：**
- 核心数据类型应该是纯粹的 POCO 类，不包含任何序列化特性
- 序列化库通过反射或源生成器来处理序列化，无需在核心类型上添加特性
- `Catga.Serialization.MemoryPack` 使用 `MemoryPackSerializer.Serialize(type, value)` 反射方法

### 2. 移除 DistributedLock.WaitHandles 依赖

**修改的文件：**
- `src/Catga/Catga.csproj` - 移除 DistributedLock.WaitHandles 包引用
- `src/Catga/DependencyInjection/ResilienceServiceCollectionExtensions.cs` - 移除默认锁提供者注册
- `src/Catga.Persistence.InMemory/Catga.Persistence.InMemory.csproj` - 添加 DistributedLock.WaitHandles 包引用
- `src/Catga.Persistence.InMemory/DependencyInjection/InMemoryPersistenceServiceCollectionExtensions.cs` - 添加 `AddWaitHandleDistributedLock()` 方法

**设计原则：**
- 核心库只依赖 `DistributedLock.Core`（抽象接口）
- 具体的锁实现由各实现库提供
- 用户必须显式选择锁实现（FileSystem、WaitHandles、Redis 等）

### 3. 修复编译错误

**修改的文件：**
- `src/Catga/Flow/DslFlowExecutor.cs` - 修复数组切片类型推断问题
- `src/Catga/Flow/StoredSnapshot.cs` - 修复构造函数调用
- `tests/Catga.Tests/Pipeline/AttributeDrivenBehaviorTests.cs` - 添加 WaitHandles using

## 最终结果

### 核心库依赖（Catga.csproj）

```
顶级包                                                     
> DistributedLock.Core                                  1.0.8
> Microsoft.Extensions.Diagnostics.HealthChecks         9.0.0
> Microsoft.Extensions.Hosting.Abstractions             10.0.0
> Polly                                                 8.5.0
> Polly.RateLimiting                                    8.5.0
> PolySharp                                             1.15.0
```

✅ 没有 MemoryPack
✅ 没有 DistributedLock.WaitHandles
✅ 只有必要的抽象和基础设施

### 实现库依赖

**Catga.Serialization.MemoryPack:**
- MemoryPack ✅

**Catga.Persistence.InMemory:**
- DistributedLock.FileSystem ✅
- DistributedLock.WaitHandles ✅

**Catga.Persistence.Redis:**
- DistributedLock.Redis ✅
- MemoryPack ✅

## 架构优势

1. **清晰的分层** - 核心库只包含抽象，实现库包含具体实现
2. **灵活的选择** - 用户可以选择不同的序列化器和锁实现
3. **更小的依赖** - 核心库依赖更少，更容易维护
4. **AOT 友好** - 序列化库可以使用反射，不影响核心库的 AOT 兼容性
5. **测试友好** - 测试可以使用 InMemory 实现，无需外部依赖

## 迁移指南

### 对于使用 Catga 的用户

**之前（自动注册）：**
```csharp
services.AddCatgaResilience(); // 自动注册 WaitHandle 锁
```

**现在（显式注册）：**
```csharp
services.AddCatgaResilience();
services.AddInMemoryDistributedLock(); // 或 AddWaitHandleDistributedLock()
```

### 对于扩展 Catga 的开发者

如果你的代码使用了 `FlowPosition`、`StoredSnapshot` 等类型：
- 这些类型现在是纯粹的 POCO 类
- 如果需要序列化，使用 `IMessageSerializer` 接口
- 不要依赖 MemoryPack 特性

## 验证

```bash
# 构建整个解决方案
dotnet build Catga.sln

# 运行测试
dotnet test Catga.sln

# 检查核心库依赖
dotnet list src/Catga/Catga.csproj package
```

所有构建和测试都通过 ✅

## 相关文档

- [依赖管理原则](./dependency-principles.md)
- [架构概述](./README.md)
