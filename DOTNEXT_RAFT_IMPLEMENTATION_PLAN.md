# DotNext Raft 完整实现计划

**目标**: 完成 DotNext Raft 的 HTTP/gRPC 通信实现
**预计时间**: 3-4 小时
**状态**: 开始执行

---

## 📋 待完成 TODO 清单

### Phase 3.1: HTTP 通信实现（1.5 小时）
- [ ] RaftAwareMediator.cs:86 - 实现转发到 Leader
- [ ] RaftAwareMediator.cs:110 - 实现广播到其他节点
- [ ] RaftAwareMediator.cs:207 - 实现实际 HTTP 转发
- [ ] RaftMessageTransport.cs:87 - 实现 HTTP/gRPC 调用
- [ ] RaftMessageTransport.cs:186 - 实现实际转发
- [ ] RaftMessageTransport.cs:199 - 实现本地处理

### Phase 3.2: Raft 配置完成（1 小时）
- [ ] DotNextClusterExtensions.cs:74 - 完成 Raft HTTP 集群配置
- [ ] DotNextClusterExtensions.cs:105 - 添加 Raft 健康检查

### Phase 3.3: 订阅逻辑（0.5 小时）
- [ ] RaftMessageTransport.cs:114 - 实现订阅逻辑

---

## 🎯 实现策略

### 1. HTTP 通信方式
使用 `HttpClient` 进行节点间通信：
```csharp
POST /catga/forward
Content-Type: application/json
{
  "messageType": "CreateOrderCommand",
  "payload": "...",
  "metadata": { ... }
}
```

### 2. 消息转发流程
```
Client → Local Node → Leader Node → Handler
         ↓ (if not leader)
         Forward via HTTP
```

### 3. 事件广播流程
```
Event Publisher → Leader → HTTP Broadcast → All Nodes
```

---

## 🔧 技术实现

### HttpClient 配置
```csharp
services.AddHttpClient<RaftMessageForwarder>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### 转发端点
```csharp
app.MapPost("/catga/forward", async (
    [FromBody] ForwardRequest request,
    [FromServices] ICatgaMediator mediator) =>
{
    // 处理转发请求
});
```

### 健康检查
```csharp
services.AddHealthChecks()
    .AddCheck<RaftHealthCheck>("raft");
```

---

## ⏱️ 预计时间线

| 任务 | 时间 | 累计 |
|------|------|------|
| Phase 3.1: HTTP 通信 | 1.5h | 1.5h |
| Phase 3.2: Raft 配置 | 1.0h | 2.5h |
| Phase 3.3: 订阅逻辑 | 0.5h | 3.0h |
| 测试和调试 | 0.5h | 3.5h |
| 文档更新 | 0.5h | 4.0h |

---

## 开始执行！

