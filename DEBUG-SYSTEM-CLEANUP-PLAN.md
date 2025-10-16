# Debug 系统清理计划

## 🔍 问题诊断

### 发现的问题

1. **两套调试系统并存**
   - 旧系统：`src/Catga/Debugging/` + `.WithDebug()`
   - 新系统：`src/Catga.Debugger/` + `AddCatgaDebuggerWithAspNetCore()`

2. **功能重复**
   - 都有消息流追踪
   - 都有 Pipeline Behavior
   - 都有调试端点
   - 都有元数据提取

3. **使用混乱**
   - `Program.cs` 中同时使用了两个系统
   - 文档中混合提到两个系统
   - 用户不知道该用哪个

4. **命名冲突**
   - `DebugOptions` vs `ReplayOptions`
   - `DebugPipelineBehavior` vs `ReplayableEventCapturer`
   - `WithDebug()` vs `AddCatgaDebugger()`

---

## 📋 系统对比

### 旧系统（`Catga.Debugging`）

**位置**：
- `src/Catga/Debugging/`
  - `MessageFlowTracker.cs`
  - `DebugPipelineBehavior.cs`
  - `ConsoleFlowFormatter.cs`
- `src/Catga.InMemory/DependencyInjection/DebugExtensions.cs`
- `src/Catga.AspNetCore/DebugEndpointExtensions.cs`

**功能**：
- ✅ 简单的消息流追踪
- ✅ 控制台输出
- ✅ HTTP API 端点（`/debug/flows`, `/debug/stats`）
- ✅ 实时追踪（基于内存）

**优点**：
- 简单，低开销
- 易于使用（一行 `.WithDebug()`）
- AOT 兼容

**缺点**：
- 功能有限
- 无时间旅行
- 无回放功能
- 无 UI

---

### 新系统（`Catga.Debugger`）

**位置**：
- `src/Catga.Debugger/` - 核心库
- `src/Catga.Debugger.AspNetCore/` - ASP.NET Core 集成 + Vue UI

**功能**：
- ✅ 时间旅行调试
- ✅ 完整回放（宏观/微观）
- ✅ Vue 3 UI
- ✅ 状态快照
- ✅ 变量捕获
- ✅ 调用栈
- ✅ SignalR 实时更新
- ✅ 自适应采样
- ✅ Ring Buffer

**优点**：
- 功能强大
- 现代化 UI
- 生产级设计
- 详细的诊断信息

**缺点**：
- 复杂度高
- 部分功能不兼容 AOT（SignalR）
- 需要更多配置

---

## 🎯 解决方案

### 方案 1：合并为一个系统（推荐）

**目标**：保留新系统，移除旧系统，但提供简化的 API。

#### 步骤：

1. **移除旧系统代码**
   - 删除 `src/Catga/Debugging/`
   - 删除 `src/Catga.InMemory/DependencyInjection/DebugExtensions.cs`
   - 删除 `src/Catga.AspNetCore/DebugEndpointExtensions.cs`

2. **简化新系统 API**
   - 在 `Catga.Debugger` 中添加 `.WithDebug()` 扩展
   - 提供简化配置（自动选择开发/生产模式）

3. **统一命名**
   - `AddCatgaDebugger()` - 核心调试功能
   - `AddCatgaDebuggerWithAspNetCore()` - 包含 UI
   - `.WithDebug()` - 快捷方法（内部调用 `AddCatgaDebugger`）

4. **更新文档**
   - 统一调试文档
   - 明确 API 用法
   - 更新所有示例

---

### 方案 2：保留两个系统（不推荐）

**目标**：明确区分两个系统的用途。

- 旧系统 → 重命名为 "Simple Debug" / "Flow Tracing"
- 新系统 → 保持为 "Time-Travel Debugger"

**缺点**：
- 用户困惑
- 维护成本高
- 代码重复

---

## 🚀 实施计划（方案 1）

### Phase 1: 代码清理

1. **删除旧系统文件**
   ```
   DELETE: src/Catga/Debugging/
   DELETE: src/Catga.InMemory/DependencyInjection/DebugExtensions.cs
   DELETE: src/Catga.AspNetCore/DebugEndpointExtensions.cs
   DELETE: src/Catga.Persistence.Redis/RedisDebugMetadata.cs
   DELETE: src/Catga.Transport.Nats/NatsDebugMetadata.cs
   DELETE: benchmarks/Catga.Benchmarks/DebugBenchmarks.cs
   ```

2. **移除相关引用**
   - 从所有项目中移除 `using Catga.Debugging`
   - 从测试中移除相关测试

### Phase 2: API 简化

1. **添加简化扩展到 `Catga.Debugger`**
   ```csharp
   // src/Catga.Debugger/DependencyInjection/CatgaBuilderDebugExtensions.cs
   public static class CatgaBuilderDebugExtensions
   {
       /// <summary>
       /// Enable debugging - auto-detects environment
       /// </summary>
       public static CatgaServiceBuilder WithDebug(this CatgaServiceBuilder builder)
       {
           var isDevelopment = /* detect environment */;
           
           if (isDevelopment)
           {
               builder.Services.AddCatgaDebuggerForDevelopment();
           }
           else
           {
               builder.Services.AddCatgaDebuggerForProduction();
           }
           
           return builder;
       }
   }
   ```

2. **统一命名约定**
   - `AddCatgaDebugger()` - 基础调试
   - `AddCatgaDebuggerWithAspNetCore()` - 包含 UI 和 SignalR
   - `.WithDebug()` - 快捷方法（CatgaBuilder 扩展）

### Phase 3: 修复示例

1. **更新 OrderSystem.Api/Program.cs**
   ```csharp
   // 简化为一个调用
   builder.Services
       .AddCatga()
       .UseMemoryPack()
       .WithDebug()  // 自动检测环境
       .ForDevelopment();
   
   // 如果需要 UI
   if (builder.Environment.IsDevelopment())
   {
       builder.Services.AddCatgaDebuggerWithAspNetCore();
       // ... 后面映射 UI
       app.MapCatgaDebugger("/debug");
   }
   ```

2. **添加清晰的注释**
   ```csharp
   // WithDebug() - 基础调试（控制台日志，API端点）
   // AddCatgaDebuggerWithAspNetCore() - 完整 UI + 时间旅行
   ```

### Phase 4: 文档更新

1. **更新 README.md**
   - 移除旧 API 引用
   - 统一为新系统
   - 简化示例

2. **更新 docs/DEBUGGER.md**
   - 完整的 API 参考
   - 清晰的使用场景
   - 简单 vs 高级模式

3. **更新 QUICK-START.md**
   - 使用 `.WithDebug()` 作为默认
   - 可选：添加 UI

4. **更新 QUICK-REFERENCE.md**
   - 统一的 API 调用
   - 清晰的配置选项

---

## 🔍 API 设计（最终版）

### 基础用法

```csharp
// 1. 最简单 - 自动检测环境
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .WithDebug()  // 开发环境 = 详细调试，生产环境 = 最小开销
    .ForDevelopment();

// 访问: GET /debug-api/flows
// 访问: GET /debug-api/stats
```

### 开发环境 + UI

```csharp
// 2. 开发环境 - 完整 UI
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForDevelopment();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapCatgaDebugger("/debug");  // Vue UI + SignalR
}

// 访问: http://localhost:5000/debug (Vue UI)
// 访问: GET /debug-api/flows
```

### 生产环境

```csharp
// 3. 生产环境 - 最小开销
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .WithDebug()  // 自动使用生产模式（0.1% 采样）
    .ForProduction();

// 仍可访问 API（低开销）
// GET /debug-api/flows
```

### 自定义配置

```csharp
// 4. 高级 - 自定义配置
builder.Services.AddCatgaDebugger(options =>
{
    options.SamplingRate = 0.5;  // 50% 采样
    options.CaptureVariables = true;
    options.CaptureCallStacks = false;
});

// 或者使用 ASP.NET Core 版本
builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
{
    // ...
});
```

---

## ✅ 测试清单

- [ ] 删除所有旧系统文件
- [ ] 移除旧系统引用
- [ ] 添加 `.WithDebug()` 扩展
- [ ] 更新 OrderSystem 示例
- [ ] 编译通过
- [ ] 运行单元测试
- [ ] 验证调试功能工作
- [ ] 验证 UI 工作
- [ ] 更新所有文档
- [ ] 验证文档中的代码示例

---

## 📊 影响范围

### 需要修改的文件

**删除**：
- `src/Catga/Debugging/*` (3 files)
- `src/Catga.InMemory/DependencyInjection/DebugExtensions.cs`
- `src/Catga.AspNetCore/DebugEndpointExtensions.cs`
- `src/Catga.Persistence.Redis/RedisDebugMetadata.cs`
- `src/Catga.Transport.Nats/NatsDebugMetadata.cs`
- `benchmarks/Catga.Benchmarks/DebugBenchmarks.cs`

**新增**：
- `src/Catga.Debugger/DependencyInjection/CatgaBuilderDebugExtensions.cs`

**修改**：
- `examples/OrderSystem.Api/Program.cs`
- `README.md`
- `docs/DEBUGGER.md`
- `docs/QUICK-START.md`
- `docs/QUICK-REFERENCE.md`
- `docs/INDEX.md`

---

## 🎯 预期结果

### 用户体验

**之前**（混乱）：
```csharp
builder.Services.AddCatga().WithDebug();  // 这是什么？
builder.Services.AddCatgaDebuggerWithAspNetCore();  // 这又是什么？
app.MapCatgaDebugEndpoints();  // ？？
app.MapCatgaDebugger("/debug");  // ？？？
```

**之后**（清晰）：
```csharp
// 简单场景
builder.Services.AddCatga().WithDebug();

// 需要 UI
builder.Services.AddCatgaDebuggerWithAspNetCore();
app.MapCatgaDebugger("/debug");
```

### 文档清晰度

- ✅ 只有一套调试系统
- ✅ 清晰的 API 层次
- ✅ 明确的使用场景
- ✅ 统一的命名

---

## ⏱️ 预计时间

- Phase 1（代码清理）：30 分钟
- Phase 2（API 简化）：30 分钟
- Phase 3（修复示例）：15 分钟
- Phase 4（文档更新）：30 分钟
- 测试验证：15 分钟

**总计：约 2 小时**

---

**开始执行？**

