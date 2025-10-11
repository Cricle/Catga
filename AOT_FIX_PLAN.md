# Catga AOT 修复计划

**日期**: 2025-10-11  
**目标**: 修复所有 AOT 相关警告，确保完全兼容 Native AOT

---

## 📊 当前问题分析

### 警告类型统计

| 警告类型 | 数量 | 严重性 | 说明 |
|---------|------|--------|------|
| IL2095 | 8 | 高 | `DynamicallyAccessedMembersAttribute` 不匹配 |
| IL3051 | 8 | 高 | `RequiresDynamicCodeAttribute` 不匹配 |
| IL2046 | 8 | 高 | `RequiresUnreferencedCodeAttribute` 不匹配 |
| IL2026 | ~10 | 中 | 使用 `JsonSerializer` 未指定 JsonTypeInfo |
| IL3050 | ~10 | 中 | 使用 `JsonSerializer` 不支持 AOT |

**总计**: ~44 个 AOT 警告

### 问题分布

#### 1. **Catga.Distributed** - DistributedMediator.cs
- **问题**: 实现类的特性标注与接口不匹配
- **影响**: 所有 Mediator 方法（SendAsync, PublishAsync, BroadcastAsync 等）
- **警告**: IL2095, IL3051, IL2046

#### 2. **Catga.Persistence.Redis** - RedisDistributedCache.cs
- **问题**: 使用 `JsonSerializer.Serialize/Deserialize` 未指定类型信息
- **影响**: 缓存的序列化/反序列化
- **警告**: IL2026, IL3050

#### 3. **Catga.Distributed.Nats** - 节点发现
- **问题**: 使用 `JsonSerializer` 序列化 NodeInfo
- **影响**: 节点信息的序列化
- **警告**: IL2026, IL3050

#### 4. **Catga.Distributed.Redis** - 节点发现和传输
- **问题**: 使用 `JsonSerializer` 序列化节点和消息
- **影响**: 分布式通信
- **警告**: IL2026, IL3050

---

## 🎯 修复策略

### 策略 1: 特性标注对齐 ✅ 简单
**适用于**: DistributedMediator

修复 `IDistributedMediator` 接口，添加缺失的特性标注，使其与实现类一致。

**优势**:
- 修复简单，只需添加特性
- 不影响现有代码逻辑
- 立即消除 24 个警告

**实施**:
```csharp
// 在 IDistributedMediator 接口上添加特性
Task<CatgaResult<TResponse>> SendToNodeAsync<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
    TResponse>(
    TRequest request, 
    string nodeId, 
    CancellationToken cancellationToken = default)
    where TRequest : IRequest<TResponse>;
```

### 策略 2: JSON Source Generator 🔧 中等
**适用于**: 所有 JsonSerializer 使用

使用 System.Text.Json 的 Source Generator 生成 AOT 兼容的序列化代码。

**步骤**:
1. 创建 `JsonSerializerContext` 类
2. 注册所有需要序列化的类型
3. 替换所有 `JsonSerializer` 调用

**优势**:
- 完全 AOT 兼容
- 性能更好（编译时生成代码）
- 减少运行时反射

**实施**:
```csharp
// 1. 创建序列化上下文
[JsonSerializable(typeof(NodeInfo))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class CatgaJsonContext : JsonSerializerContext
{
}

// 2. 使用上下文
var json = JsonSerializer.Serialize(node, CatgaJsonContext.Default.NodeInfo);
var node = JsonSerializer.Deserialize(json, CatgaJsonContext.Default.NodeInfo);
```

### 策略 3: 消息序列化抽象 🔧 复杂
**适用于**: 分布式消息序列化

创建抽象层，允许用户选择序列化器（JSON Source Gen, MemoryPack 等）。

**优势**:
- 灵活性高
- 用户可选择最佳序列化器
- 支持多种场景

**实施**:
```csharp
public interface IDistributedSerializer
{
    string Serialize<T>(T value);
    T? Deserialize<T>(string value);
}

// JSON Source Generator 实现
public class JsonSourceGenSerializer : IDistributedSerializer
{
    // 使用 JsonSerializerContext
}

// MemoryPack 实现（更高性能）
public class MemoryPackDistributedSerializer : IDistributedSerializer
{
    // 使用 MemoryPack
}
```

---

## 📋 实施计划

### 阶段 1: 快速修复（高优先级）⚡

**目标**: 消除 DistributedMediator 的 24 个警告

#### 任务 1.1: 更新 IDistributedMediator 接口
- [ ] 添加 `[DynamicallyAccessedMembers]` 特性到泛型参数
- [ ] 添加 `[RequiresDynamicCode]` 到接口方法
- [ ] 添加 `[RequiresUnreferencedCode]` 到接口方法
- [ ] 验证编译无警告

**文件**: `src/Catga.Distributed/IDistributedMediator.cs`

**估计时间**: 30 分钟

#### 任务 1.2: 更新 ICatgaMediator 接口
- [ ] 确认接口已有正确特性（在 DistributedMediator 实现中也警告）
- [ ] 如需要，添加缺失特性

**文件**: `src/Catga/ICatgaMediator.cs`

**估计时间**: 15 分钟

---

### 阶段 2: JSON Source Generator（中优先级）🔧

**目标**: 为所有 JSON 序列化创建 AOT 兼容的实现

#### 任务 2.1: 创建 Catga.Distributed JSON Context
- [ ] 创建 `DistributedJsonContext.cs`
- [ ] 注册 `NodeInfo`
- [ ] 注册 `NodeChangeEvent`
- [ ] 注册 `Dictionary<string, string>`
- [ ] 注册 `Dictionary<string, object>`

**文件**: `src/Catga.Distributed/Serialization/DistributedJsonContext.cs`

**估计时间**: 30 分钟

#### 任务 2.2: 更新 NATS 节点发现
- [ ] `NatsNodeDiscovery.cs` 使用 JSON Context
- [ ] `NatsJetStreamKVNodeDiscovery.cs` 使用 JSON Context
- [ ] 删除直接的 `JsonSerializer` 调用

**文件**: 
- `src/Catga.Distributed.Nats/NodeDiscovery/NatsNodeDiscovery.cs`
- `src/Catga.Distributed.Nats/NodeDiscovery/NatsJetStreamKVNodeDiscovery.cs`

**估计时间**: 45 分钟

#### 任务 2.3: 更新 Redis 节点发现
- [ ] `RedisNodeDiscovery.cs` 使用 JSON Context
- [ ] `RedisSortedSetNodeDiscovery.cs` 使用 JSON Context
- [ ] `RedisStreamTransport.cs` 使用 JSON Context

**文件**: 
- `src/Catga.Distributed.Redis/NodeDiscovery/RedisNodeDiscovery.cs`
- `src/Catga.Distributed.Redis/NodeDiscovery/RedisSortedSetNodeDiscovery.cs`
- `src/Catga.Distributed.Redis/Transport/RedisStreamTransport.cs`

**估计时间**: 45 分钟

#### 任务 2.4: 更新 Redis 缓存
- [ ] 创建通用的 `CatgaJsonContext` 或让用户提供
- [ ] 更新 `RedisDistributedCache.cs`

**文件**: `src/Catga.Persistence.Redis/RedisDistributedCache.cs`

**估计时间**: 30 分钟

---

### 阶段 3: 验证和测试（必需）✅

#### 任务 3.1: AOT 发布测试
- [ ] 创建简单的 AOT 测试项目
- [ ] 测试分布式 NATS 功能
- [ ] 测试分布式 Redis 功能
- [ ] 测试序列化/反序列化

**估计时间**: 1 小时

#### 任务 3.2: 基准测试
- [ ] 对比 JSON Source Gen vs 反射序列化性能
- [ ] 对比 AOT vs JIT 性能
- [ ] 更新 Benchmark 项目

**估计时间**: 1 小时

#### 任务 3.3: 文档更新
- [ ] 更新 README - AOT 支持说明
- [ ] 创建 AOT 使用指南
- [ ] 更新示例项目

**估计时间**: 1 小时

---

## 🔍 详细技术方案

### 方案 1: DistributedMediator 特性修复

#### 问题根源
```csharp
// 接口（无特性）
public interface IDistributedMediator
{
    Task<CatgaResult<TResponse>> SendToNodeAsync<TRequest, TResponse>(...)
        where TRequest : IRequest<TResponse>;
}

// 实现（有特性）
public class DistributedMediator : IDistributedMediator
{
    [RequiresDynamicCode("...")]
    [RequiresUnreferencedCode("...")]
    public async Task<CatgaResult<TResponse>> SendToNodeAsync<
        [DynamicallyAccessedMembers(...)] TRequest, 
        TResponse>(...)
    {
        // 实现
    }
}
```

#### 修复方案
```csharp
// 接口添加相同特性
public interface IDistributedMediator
{
    [RequiresDynamicCode("Distributed mediator uses reflection for message routing")]
    [RequiresUnreferencedCode("Distributed mediator may require types that cannot be statically analyzed")]
    Task<CatgaResult<TResponse>> SendToNodeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        TRequest request,
        string nodeId,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;
        
    // 其他方法类似...
}
```

### 方案 2: JSON Source Generator 实现

#### 创建序列化上下文

**文件**: `src/Catga.Distributed/Serialization/DistributedJsonContext.cs`

```csharp
using System.Text.Json.Serialization;

namespace Catga.Distributed.Serialization;

/// <summary>
/// JSON 序列化上下文（AOT 兼容）
/// 用于分布式节点发现和消息传递
/// </summary>
[JsonSerializable(typeof(NodeInfo))]
[JsonSerializable(typeof(NodeChangeEvent))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(MessageEnvelope))]  // 如果有
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class DistributedJsonContext : JsonSerializerContext
{
}
```

#### 使用序列化上下文

**Before**:
```csharp
var json = JsonSerializer.Serialize(node);
var node = JsonSerializer.Deserialize<NodeInfo>(json);
```

**After**:
```csharp
var json = JsonSerializer.Serialize(node, DistributedJsonContext.Default.NodeInfo);
var node = JsonSerializer.Deserialize(json, DistributedJsonContext.Default.NodeInfo);
```

#### 创建辅助扩展方法

**文件**: `src/Catga.Distributed/Serialization/JsonHelper.cs`

```csharp
namespace Catga.Distributed.Serialization;

/// <summary>
/// JSON 序列化辅助（AOT 兼容）
/// </summary>
internal static class JsonHelper
{
    public static string SerializeNode(NodeInfo node)
        => JsonSerializer.Serialize(node, DistributedJsonContext.Default.NodeInfo);
        
    public static NodeInfo? DeserializeNode(string json)
        => JsonSerializer.Deserialize(json, DistributedJsonContext.Default.NodeInfo);
        
    public static string SerializeDictionary(Dictionary<string, string> dict)
        => JsonSerializer.Serialize(dict, DistributedJsonContext.Default.DictionaryStringString);
        
    // ... 其他辅助方法
}
```

### 方案 3: Redis 缓存序列化

**问题**: `RedisDistributedCache` 需要序列化任意类型

**挑战**: 无法预先知道所有类型

**解决方案**: 让用户提供 `JsonSerializerContext`

```csharp
public class RedisDistributedCache : IDistributedCache
{
    private readonly JsonSerializerContext? _jsonContext;
    
    public RedisDistributedCache(
        IConnectionMultiplexer redis,
        JsonSerializerContext? jsonContext = null)
    {
        _jsonContext = jsonContext;
    }
    
    public async Task SetAsync<T>(string key, T value, ...)
    {
        string json;
        if (_jsonContext != null)
        {
            // 使用用户提供的上下文
            var typeInfo = _jsonContext.GetTypeInfo(typeof(T));
            json = JsonSerializer.Serialize(value, typeInfo);
        }
        else
        {
            // 降级到反射（非 AOT）
            json = JsonSerializer.Serialize(value);
        }
        
        await _db.StringSetAsync(key, json, expiry);
    }
}
```

---

## ⏱️ 时间估算

| 阶段 | 任务数 | 估计时间 | 优先级 |
|-----|-------|---------|--------|
| 阶段 1: 特性修复 | 2 | 45 分钟 | 🔴 高 |
| 阶段 2: JSON Source Gen | 4 | 2.5 小时 | 🟡 中 |
| 阶段 3: 验证测试 | 3 | 3 小时 | 🟢 必需 |
| **总计** | **9** | **~6 小时** | |

---

## 🎯 成功标准

### 编译时
- ✅ 0 个 IL2095 警告
- ✅ 0 个 IL3051 警告
- ✅ 0 个 IL2046 警告
- ✅ 0 个 IL2026 警告
- ✅ 0 个 IL3050 警告

### 运行时
- ✅ AOT 发布成功
- ✅ 所有单元测试通过
- ✅ 所有集成测试通过
- ✅ 性能不降低（或提升）

### 文档
- ✅ AOT 使用指南完整
- ✅ 示例项目可用
- ✅ 迁移指南清晰

---

## 🚀 执行顺序

### 第一步: 快速胜利 ⚡
1. 修复 `IDistributedMediator` 特性（消除 24 个警告）
2. 修复 `ICatgaMediator` 特性（如需要）
3. 提交并验证

**预期结果**: 从 44 个警告降至 ~20 个警告

### 第二步: 核心序列化 🔧
4. 创建 `DistributedJsonContext`
5. 更新 NATS 节点发现
6. 更新 Redis 节点发现和传输
7. 提交并验证

**预期结果**: 从 ~20 个警告降至 ~4 个警告（仅 RedisDistributedCache）

### 第三步: 缓存优化 🔧
8. 为 `RedisDistributedCache` 添加可选的 JsonContext 支持
9. 更新文档说明 AOT 最佳实践
10. 提交并验证

**预期结果**: 0 个警告，完全 AOT 兼容

### 第四步: 验证和文档 ✅
11. AOT 发布测试
12. 性能基准测试
13. 更新文档和示例
14. 最终提交

**预期结果**: 完整的 AOT 支持，文档完善

---

## 📝 注意事项

### AOT 限制
- 不能使用反射创建类型
- 不能使用动态代码生成
- 需要预先知道所有序列化类型

### 兼容性
- JSON Source Generator 需要 .NET 6.0+
- 现有代码（非 AOT）仍能正常工作
- 渐进式迁移，不破坏现有 API

### 性能
- JSON Source Generator 比反射快 ~2-3x
- AOT 启动速度更快
- 包大小可能稍大（包含序列化代码）

---

## 🎉 预期收益

1. **完全 AOT 兼容** ✅
   - 支持 Native AOT 发布
   - 更快的启动速度
   - 更小的内存占用

2. **性能提升** 🚀
   - JSON 序列化性能提升 2-3x
   - 减少运行时反射开销
   - 更好的 IL 剪裁

3. **更好的可预测性** 📊
   - 编译时发现序列化问题
   - 更清晰的依赖关系
   - 更好的工具支持

4. **云原生友好** ☁️
   - 更适合容器部署
   - 冷启动更快
   - 资源消耗更低

---

**状态**: 📋 **计划制定完成，等待执行**  
**下一步**: 执行阶段 1 - 快速修复特性标注

