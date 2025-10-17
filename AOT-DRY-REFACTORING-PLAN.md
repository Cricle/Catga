# AOT + DRY 重构计划
## 目标：移除所有 AOT 抑制消息，真正解决问题而非隐藏警告

---

## 📋 当前问题分析

### 1. ActivityPayloadCapture 的 AOT 问题
**问题代码：**
```csharp
// ❌ 使用 System.Text.Json (需要反射)
private static string? TryJsonSerialize<T>(T payload)
{
    return System.Text.Json.JsonSerializer.Serialize(payload); // IL2026, IL3050
}
```

**问题根源：**
- `System.Text.Json.JsonSerializer.Serialize<T>` 需要反射
- 在 AOT 环境下无法工作
- 当前用 `UnconditionalSuppressMessage` 隐藏警告

**解决方案：完全删除这个方法！**
- 不提供 fallback，强制用户设置 CustomSerializer
- 或者只序列化基本类型（ToString）

---

### 2. DistributedTracingBehavior 的 GetCorrelationId
**问题代码：**
```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026:...")]
[UnconditionalSuppressMessage("Trimming", "IL2075:...")]
private static string GetCorrelationId(TRequest request)
{
    // ...反射访问 CorrelationIdMiddleware
    var middlewareType = Type.GetType("Catga.AspNetCore.Middleware.CorrelationIdMiddleware, Catga.AspNetCore");
    var currentProperty = middlewareType?.GetProperty("Current", ...);
    var globalId = currentProperty?.GetValue(null) as string;
    // ...
}
```

**问题根源：**
- 使用 `Type.GetType()` 反射
- 使用 `GetProperty()` 和 `GetValue()` 反射
- AOT 无法保证类型存在

**解决方案：完全删除反射 fallback！**
- 只保留 Activity.Baggage 和 IMessage.CorrelationId
- 移除 CorrelationIdMiddleware 的反射访问

---

## 🎯 重构阶段

### Phase 1: 重构 ActivityPayloadCapture（移除反射）✅

**方案 A：完全删除 TryJsonSerialize**
```csharp
public static void CapturePayload<T>(Activity? activity, string tagName, T payload)
{
    if (activity == null || payload == null) return;

    string? json = null;

    // Only use custom serializer (AOT-safe)
    if (CustomSerializer != null)
    {
        try { json = CustomSerializer(payload); }
        catch { activity.SetTag(tagName, "<serialization error>"); return; }
    }
    else
    {
        // No custom serializer - use ToString (always AOT-safe)
        json = payload.ToString();
    }

    if (json != null && json.Length <= MaxPayloadLength)
        activity.SetTag(tagName, json);
    else if (json != null)
        activity.SetTag(tagName, $"<too large: {json.Length} bytes>");
}
```

**优点：**
- ✅ 0 AOT 警告
- ✅ 不需要任何抑制消息
- ✅ ToString() 始终可用且 AOT 安全
- ✅ 用户可以通过 CustomSerializer 获得更好的序列化

**缺点：**
- 默认情况下只能看到 ToString() 的输出（通常不太有用）

---

### Phase 2: 重构 DistributedTracingBehavior.GetCorrelationId（移除反射）✅

**方案：删除反射 fallback**
```csharp
private static string GetCorrelationId(TRequest request)
{
    // 1. Try Activity.Baggage (AOT-safe, distributed tracing)
    var baggageId = Activity.Current?.GetBaggageItem("catga.correlation_id");
    if (!string.IsNullOrEmpty(baggageId))
        return baggageId;

    // 2. Try IMessage interface (AOT-safe)
    if (request is IMessage message && !string.IsNullOrEmpty(message.CorrelationId))
        return message.CorrelationId;

    // 3. Generate new ID (no fallback to middleware reflection)
    return Guid.NewGuid().ToString("N");
}
```

**优点：**
- ✅ 0 AOT 警告
- ✅ 不需要任何抑制消息
- ✅ 100% AOT 兼容
- ✅ 依赖标准接口和 API

**缺点：**
- 如果 Activity.Baggage 和 IMessage 都没有设置，会生成新的 ID
- 但这是正确行为：用户应该正确传播 CorrelationId

---

### Phase 3: 扫描并修复其他抑制消息 ✅

**已发现的其他抑制消息文件：**
1. `src\Catga.Persistence.Redis\Persistence\RedisInboxPersistence.cs`
2. `src\Catga.Persistence.Redis\Persistence\RedisOutboxPersistence.cs`
3. `src\Catga.Persistence.Redis\OptimizedRedisOutboxStore.cs`
4. `src\Catga.InMemory\CatgaExceptionJsonConverter.cs`
5. `src\Catga.InMemory\SerializationHelper.cs`
6. `src\Catga.InMemory\DependencyInjection\CatgaBuilder.cs`
7. `src\Catga.InMemory\Stores\InMemoryDeadLetterQueue.cs`
8. `src\Catga.InMemory\Stores\ShardedIdempotencyStore.cs`
9. `src\Catga.Serialization.Json\JsonMessageSerializer.cs`
10. `src\Catga.Persistence.Redis\RedisDistributedCache.cs`
11. `src\Catga.Persistence.Redis\RedisIdempotencyStore.cs`
12. `src\Catga.Transport.Nats\NatsMessageTransport.cs`
13. `src\Catga.InMemory\Pipeline\Behaviors\IdempotencyBehavior.cs`
14. `src\Catga.AspNetCore\CatgaEndpointExtensions.cs`

**策略：**
- 检查每个文件的抑制原因
- 分类：
  - **合理抑制**：序列化库（JSON/Redis），必须支持动态类型，保留但添加清晰注释
  - **可移除抑制**：可以通过重构解决的，立即修复
  - **DI 相关**：Source Generator 应该处理的，标记为需要增强 Generator

---

## 📊 执行优先级

### 🔴 P0: 立即修复（不增加代码）
1. **ActivityPayloadCapture**: 移除 `TryJsonSerialize`，使用 `ToString()` 作为 fallback
2. **DistributedTracingBehavior**: 移除 `GetCorrelationId` 的反射 fallback

### 🟡 P1: 审查现有抑制
3. 检查 14 个文件的抑制消息
4. 分类为：合理保留 / 需要修复 / 需要 Generator 增强

### 🟢 P2: 文档和验证
5. 更新文档说明 AOT 要求
6. 运行 `dotnet publish /p:PublishAot=true` 验证
7. 确保 0 AOT 警告

---

## ✅ 预期成果

### 代码质量
- ✅ 移除所有不必要的抑制消息
- ✅ 真正解决 AOT 兼容问题而非隐藏
- ✅ 代码更简单（删除反射代码）
- ✅ DRY 原则得到维持

### AOT 兼容性
- ✅ 0 AOT 警告（除了合理的序列化库）
- ✅ 100% Native AOT 可发布
- ✅ 所有关键路径 AOT 安全

### 用户体验
- ✅ 清晰的 AOT 要求文档
- ✅ 简单的 CustomSerializer 配置
- ✅ 更好的错误消息

---

## 🚀 执行步骤

1. **Phase 1**: 重构 `ActivityPayloadCapture.cs` - 删除 `TryJsonSerialize`
2. **Phase 2**: 重构 `DistributedTracingBehavior.cs` - 删除 `GetCorrelationId` 反射
3. **Phase 3**: 扫描其他 14 个文件，分类处理
4. **Phase 4**: 编译验证，确保 0 警告
5. **Phase 5**: 发布测试 `dotnet publish /p:PublishAot=true`

---

**原则：真正解决问题，而不是隐藏警告！**

