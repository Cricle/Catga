# Catga 依赖管理原则

## 核心原则

Catga 采用分层架构，遵循以下依赖管理原则：

### 1. 核心库最小化依赖

**Catga** 核心库应该保持最小的外部依赖，只包含：
- 抽象接口和基础类型
- 必要的 Microsoft.Extensions.* 抽象
- 弹性框架（Polly）
- 分布式锁抽象（DistributedLock.Core）

**核心库不应该依赖具体实现库**，如 MemoryPack、DistributedLock.WaitHandles 等。

### 2. 实现库包含具体依赖

具体的实现应该在专门的库中：

| 功能 | 核心库 | 实现库 |
|------|--------|--------|
| 序列化 | 接口 `IMessageSerializer` | `Catga.Serialization.MemoryPack` |
| 分布式锁 | 接口 `IDistributedLockProvider` | `Catga.Persistence.InMemory`<br/>`Catga.Persistence.Redis` |
| 持久化 | 接口 `IEventStore` 等 | `Catga.Persistence.Redis`<br/>`Catga.Persistence.Nats` |
| 传输 | 接口 `IMessageBus` | `Catga.Transport.Redis`<br/>`Catga.Transport.Nats` |

## 数据模型设计

### 核心数据类型不依赖序列化库

核心库中的数据模型（如 `FlowPosition`, `StoredSnapshot`, `WaitCondition`）应该是纯粹的 POCO 类，不包含任何序列化特性：

```csharp
// ✅ 正确：纯粹的数据类
public record FlowPosition
{
    public int[] Path { get; init; }
    public FlowPosition(int[] path) => Path = path ?? [0];
}

// ❌ 错误：依赖序列化库
[MemoryPackable]  // 不应该在核心库中使用
public partial record FlowPosition { ... }
```

### 序列化库负责序列化逻辑

序列化库通过反射或源生成器来处理核心类型的序列化：

```csharp
// Catga.Serialization.MemoryPack
public class MemoryPackMessageSerializer : MessageSerializerBase
{
    public override byte[] Serialize(object value, Type type)
    {
        // 使用反射序列化，无需特性
        return MemoryPackSerializer.Serialize(type, value)!;
    }
}
```

## 分布式锁的实现

核心库只依赖 `DistributedLock.Core`（抽象），具体实现由各持久化库提供：

- **Catga.Persistence.InMemory**: 
  - `DistributedLock.FileSystem` - 文件系统锁（推荐用于开发）
  - `DistributedLock.WaitHandles` - 进程内锁（仅测试用）
  
- **Catga.Persistence.Redis**: 
  - `DistributedLock.Redis` - Redis 分布式锁

- **Catga.Cluster**: 
  - 使用 DotNext.Threading 的 Raft 共识锁

## 依赖注册

### 核心库不提供默认实现

```csharp
// ❌ 错误：核心库不应该注册具体实现
services.TryAddSingleton<IDistributedLockProvider>(
    new WaitHandleDistributedSynchronizationProvider());

// ✅ 正确：由实现库注册
// 在 Catga.Persistence.InMemory 中：
services.AddInMemoryDistributedLock();
// 或
services.AddWaitHandleDistributedLock(); // 仅测试用
```

### 实现库负责完整配置

每个实现库应该提供完整的 DI 扩展方法：

```csharp
// Catga.Persistence.InMemory
services.AddInMemoryPersistence(); // 包含所有必要的服务

// Catga.Persistence.Redis
services.AddRedisPersistence(options => { ... });

// Catga.Transport.Nats
services.AddNatsTransport(options => { ... });
```

## 测试依赖

测试项目可以直接依赖实现库：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Catga\Catga.csproj" />
  <ProjectReference Include="..\..\src\Catga.Persistence.InMemory\Catga.Persistence.InMemory.csproj" />
  <ProjectReference Include="..\..\src\Catga.Transport.InMemory\Catga.Transport.InMemory.csproj" />
</ItemGroup>
```

## 架构验证

定期检查依赖关系：

```bash
# 检查核心库的依赖
dotnet list src/Catga/Catga.csproj package

# 确保没有引入不必要的具体实现
```

## 总结

- **核心库**：抽象 + 最小依赖 + 纯粹的数据模型
- **实现库**：具体实现 + 完整配置 + 序列化逻辑
- **用户选择**：根据需求选择实现库组合
- **测试友好**：InMemory 实现用于开发和测试
- **序列化无关**：核心数据类型不依赖任何序列化库

