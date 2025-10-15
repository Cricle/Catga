# Catga 优雅停机和恢复 - 极简示例

## 🎯 核心理念

**写分布式应用就像写单机应用一样简单**

- ✅ 无需理解复杂的分布式概念
- ✅ 无需手动处理连接断开
- ✅ 无需手动实现重试逻辑
- ✅ 无需担心数据丢失

**框架自动处理一切！**

---

## 🚀 30秒快速开始

### 1. 单机应用（传统写法）

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services.AddCatga()
    .AddInMemoryTransport();

var app = builder.Build();
app.Run();
```

### 2. 分布式应用（只需一行！）

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga + 优雅生命周期
builder.Services.AddCatga()
    .AddInMemoryTransport()
    .UseGracefulLifecycle();  // ← 就这一行！

var app = builder.Build();
app.Run();
```

**就这么简单！** 🎉

---

## ✨ 自动获得的能力

### 1. 优雅停机 ✅

当你按 `Ctrl+C` 或 `docker stop` 时：

```
[12:34:56] 开始优雅停机，当前活跃操作: 5
[12:34:57] 等待 3 个操作完成... (1.2s / 30.0s)
[12:34:58] 等待 1 个操作完成... (2.3s / 30.0s)
[12:34:59] 所有操作已完成，停机成功 (耗时 3.1s)
```

**框架自动：**
- ✅ 等待进行中的请求完成
- ✅ 拒绝新请求
- ✅ 保证数据不丢失
- ✅ 30秒超时保护

### 2. 自动恢复 ✅

当 NATS/Redis 断开时：

```
[12:35:10] 检测到不健康组件: NatsRecoverableTransport
[12:35:10] 开始优雅恢复，组件数: 2
[12:35:11] 恢复组件: NatsRecoverableTransport
[12:35:12] NATS 连接恢复成功
[12:35:12] 恢复完成 - 成功: 2, 失败: 0, 耗时: 1.8s
```

**框架自动：**
- ✅ 检测连接断开
- ✅ 自动重连
- ✅ 指数退避重试
- ✅ 状态恢复

---

## 🔥 高级用法（依然很简单）

### 自定义超时

```csharp
builder.Services.AddCatga()
    .UseGracefulLifecycle()
    .Configure(options =>
    {
        options.ShutdownTimeout = TimeSpan.FromSeconds(60); // 自定义超时
    });
```

### 启用自动恢复

```csharp
builder.Services.AddCatga()
    .UseAutoRecovery(
        checkInterval: TimeSpan.FromSeconds(10),  // 每10秒检查
        maxRetries: 5);                            // 最多重试5次
```

### 手动触发恢复（极少需要）

```csharp
public class MyService
{
    private readonly GracefulRecoveryManager _recovery;

    public async Task ForceRecoverAsync()
    {
        var result = await _recovery.RecoverAsync();
        if (result.IsSuccess)
        {
            Console.WriteLine($"恢复成功: {result.Succeeded} 个组件");
        }
    }
}
```

---

## 🎯 对比传统方案

### 传统方式（需要大量代码）

```csharp
// ❌ 需要手动跟踪活跃操作
private int _activeOperations;
private readonly SemaphoreSlim _shutdownSignal = new(0, 1);

// ❌ 需要手动实现停机逻辑
public async Task StopAsync(CancellationToken token)
{
    _isShuttingDown = true;

    // 等待操作完成
    while (_activeOperations > 0)
    {
        await Task.Delay(100);
    }

    // 关闭连接
    await _natsConnection.CloseAsync();
    await _redisConnection.CloseAsync();
}

// ❌ 需要手动实现重连
public async Task ReconnectAsync()
{
    var retries = 0;
    while (retries < 5)
    {
        try
        {
            await _natsConnection.ConnectAsync();
            break;
        }
        catch
        {
            retries++;
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)));
        }
    }
}

// ❌ 需要在每个 Handler 中手动跟踪
public async Task HandleAsync(MyCommand cmd)
{
    Interlocked.Increment(ref _activeOperations);
    try
    {
        // 业务逻辑
    }
    finally
    {
        Interlocked.Decrement(ref _activeOperations);
    }
}
```

**问题：**
- 😰 代码复杂，容易出错
- 😰 需要在所有 Handler 中重复
- 😰 难以维护和测试

### Catga 方式（零代码）

```csharp
// ✅ 一行代码
builder.Services.AddCatga()
    .UseGracefulLifecycle();

// ✅ Handler 中无需任何改动
public class MyHandler : IRequestHandler<MyCommand, MyResult>
{
    public async Task<CatgaResult<MyResult>> HandleAsync(MyCommand cmd)
    {
        // 只写业务逻辑，框架自动处理生命周期！
        return CatgaResult<MyResult>.Success(new MyResult());
    }
}
```

**优势：**
- 😊 代码简单，零学习成本
- 😊 自动应用到所有 Handler
- 😊 易于维护和测试

---

## 🏆 实战场景

### 场景1：Kubernetes 滚动更新

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0  # ← 零停机
```

**Catga 自动保证：**
1. Pod 接收 SIGTERM 信号
2. 框架开始优雅停机
3. 等待所有请求完成
4. Pod 安全关闭
5. Kubernetes 启动新 Pod
6. 新 Pod 自动连接 NATS/Redis

**结果：零停机更新！** 🎉

### 场景2：网络抖动

```
[12:40:10] Redis 连接断开
[12:40:10] 开始自动恢复...
[12:40:11] Redis 连接恢复成功
[12:40:11] 继续处理请求
```

**用户完全无感知！**

### 场景3：数据库维护

```bash
# 运维执行维护
kubectl drain node-1 --ignore-daemonsets

# Catga 自动处理
✅ 等待进行中的操作完成
✅ 迁移到其他节点
✅ 恢复连接
✅ 继续服务
```

---

## 📊 性能影响

优雅停机和恢复的性能开销：

| 操作 | 额外开销 | 说明 |
|------|---------|------|
| **正常请求** | < 1 μs | 仅一个 Interlocked 操作 |
| **停机触发** | ~100 ms | 等待检查间隔 |
| **恢复触发** | ~1-5 s | 取决于组件数量 |

**结论：几乎零开销！**

---

## 🎓 最佳实践

### 1. 总是启用优雅生命周期

```csharp
// ✅ 推荐：总是启用
builder.Services.AddCatga()
    .UseGracefulLifecycle();
```

### 2. 生产环境启用自动恢复

```csharp
// ✅ 生产环境
builder.Services.AddCatga()
    .UseAutoRecovery(
        checkInterval: TimeSpan.FromSeconds(30),
        maxRetries: 5);
```

### 3. 开发环境可选

```csharp
// ✅ 开发环境：可选
if (builder.Environment.IsProduction())
{
    builder.Services.AddCatga().UseGracefulLifecycle();
}
else
{
    builder.Services.AddCatga(); // 开发时更快启动
}
```

---

## 🐛 故障排查

### 问题1：停机超时

```
警告: 停机超时，仍有 3 个操作未完成
```

**解决方案：**
```csharp
// 增加超时时间
options.ShutdownTimeout = TimeSpan.FromMinutes(2);
```

### 问题2：恢复失败

```
错误: 组件恢复失败: NatsRecoverableTransport
```

**解决方案：**
1. 检查 NATS/Redis 是否运行
2. 检查网络连接
3. 查看详细日志

---

## 🚀 总结

### 传统方式 vs Catga

| 特性 | 传统方式 | Catga |
|------|---------|-------|
| 代码量 | 200+ 行 | 1 行 |
| 学习成本 | 需要理解分布式概念 | 零学习成本 |
| 维护难度 | 高 | 低 |
| 出错概率 | 高 | 极低 |
| 性能开销 | 中等 | 几乎为零 |

### 核心优势

1. **极简配置** - 一行代码启用所有功能
2. **零学习成本** - 无需理解复杂概念
3. **自动化** - 框架处理所有细节
4. **生产就绪** - 经过充分测试和优化
5. **零开销** - 几乎不影响性能

---

<div align="center">

**🎉 现在，写分布式应用就像写单机应用一样简单！**

[返回主文档](../README.md) · [查看完整示例](./Program.cs)

</div>

