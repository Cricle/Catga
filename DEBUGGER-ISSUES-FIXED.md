# Catga Debugger 问题修复报告

生成时间：2025-10-17

## ✅ 已修复的问题

### 1. 统计信息计算错误

**问题描述：**
- 成功率（SuccessRate）和平均延迟（AverageLatency）字段缺失
- 统计信息基于单个事件计算，而不是基于消息流

**修复内容：**
- ✅ 在 `StatsResponse` 中添加了 `SuccessRate` 和 `AverageLatency` 字段
- ✅ 修改了 `GetStatsAsync` 方法的计算逻辑：
  - **成功率**：按消息流（CorrelationId）分组，统计没有异常的流占比
  - **平均延迟**：仅使用 `PerformanceMetric` 类型事件的 Duration 平均值
- ✅ UI 统计面板正确显示成功率和平均延迟

**代码位置：**
- `src/Catga.Debugger.AspNetCore/Endpoints/DebuggerEndpoints.cs` (第 168-209 行)

### 2. 时间旅行标签页移除

**问题描述：**
- 时间旅行功能尚未完全实现，但标签页存在
- 用户体验不佳，功能引导不足

**修复内容：**
- ✅ 移除了"时间旅行"标签页按钮
- ✅ 删除了整个"Replay Tab"内容区域
- ✅ 简化了 UI，只保留"消息流"和"统计信息"两个标签

**代码位置：**
- `src/Catga.Debugger.AspNetCore/wwwroot/debugger/index.html`

### 3. UI 改进

**已完成的改进：**
- ✅ Duration 显示格式：使用 `.toFixed(2)` 显示 2 位小数
- ✅ 统计面板：添加"总消息流"、"存储大小"、"最早事件"展示
- ✅ 空状态引导：消息流列表为空时显示快速开始提示
- ✅ 详情面板：修复日期显示（使用 `startTime` 和 `endTime`）

## ⚠️  已知问题

### 问题：每次 API 调用记录 2 个消息流

**现象：**
```
调用 1 次 /demo/order-success
  → 增加 Events: 8
  → 增加 Flows: 2  ❌ 期望值：1
```

**根本原因：**
1. 每个请求在 Catga 框架内部被多次包装
2. `ReplayableEventCapturer` 为每个包装生成独立的 `CorrelationId`：
   ```csharp
   private string GetCorrelationId(TRequest request)
   {
       if (request is IMessage message && !string.IsNullOrEmpty(message.CorrelationId))
           return message.CorrelationId;
       
       // 问题：每次都生成新的 Guid
       return Guid.NewGuid().ToString("N");
   }
   ```

**影响范围：**
- ✅ 统计信息计算正确（因为现在按流分组计算）
- ❌ 消息流数量翻倍（应该是 N 个调用 → N 个流，实际是 N 个调用 → 2N 个流）
- ❌ UI 显示的流数量不准确

**解决方案（待实现）：**
1. **方案 A：HTTP 请求级 CorrelationId**
   - 在 ASP.NET Core 中间件层注入全局 CorrelationId
   - 使用 `AsyncLocal<string>` 或 HttpContext.Items 传递
   - 所有 Message 共享同一个 CorrelationId

2. **方案 B：Command 实现 IMessage**
   - 让 `CreateOrderCommand` 等实现 `IMessage` 接口
   - 在创建 Command 时显式设置 CorrelationId
   - 需要修改所有 Command 定义

3. **方案 C：Behavior 去重**
   - 在 `ReplayableEventCapturer` 中缓存已处理的请求
   - 使用 Request 的哈希值或其他唯一标识去重
   - 风险：可能漏掉真正重复的请求

**推荐方案：A**（最干净，最符合分布式追踪标准）

## 📊 测试验证

### 测试脚本

创建了 `test-flow-count.ps1` 用于验证流计数准确性：

```powershell
# 测试 1 次调用
调用 /demo/order-success
  → 检查增加的 Events 和 Flows

# 测试 2 次调用
再次调用 /demo/order-success
  → 再次检查增加的 Events 和 Flows

# 验证
每次调用应该增加 1 个流（目前增加 2 个）
```

### 当前测试结果

```
1️⃣  调用 1 次
   增加 Events: 8
   增加 Flows: 2  ❌ 期望：1

2️⃣  调用 2 次
   增加 Events: 8
   增加 Flows: 2  ❌ 期望：1

总事件数: 16
总流数: 4  ❌ 期望：2
成功率: 100%  ✅
平均延迟: 27.19ms  ✅
```

## 📝 后续工作

### 高优先级
- [ ] 实现全局 CorrelationId 管理（方案 A）
- [ ] 验证修复后流计数正确（每次调用 → 1 个流）

### 中优先级
- [ ] 完善消息流详情页面（显示事件列表）
- [ ] 添加流筛选功能（按状态、消息类型）

### 低优先级
- [ ] 实现时间旅行回放功能（完整实现）
- [ ] 添加性能趋势图表

## 📚 相关文件

### 修改的文件
1. `src/Catga.Debugger.AspNetCore/Endpoints/DebuggerEndpoints.cs`
   - 统计计算逻辑修复
2. `src/Catga.Debugger.AspNetCore/wwwroot/debugger/index.html`
   - UI 改进和时间旅行标签移除
3. `test-flow-count.ps1`
   - 新增测试脚本

### 相关代码
- `src/Catga.Debugger/Pipeline/ReplayableEventCapturer.cs` - 事件捕获（CorrelationId 生成）
- `src/Catga.InMemory/CatgaMediator.cs` - Mediator 执行流程
- `examples/OrderSystem.Api/Program.cs` - 示例应用配置

## 🎯 总结

本次修复解决了统计信息计算错误和 UI 问题，但仍存在**消息流重复记录**的核心问题。

**统计信息现在是正确的**（按流计算成功率），但**流的数量不准确**（翻倍）。

**下一步**：实现全局 CorrelationId 管理机制，确保同一个 HTTP 请求只产生一个消息流。

