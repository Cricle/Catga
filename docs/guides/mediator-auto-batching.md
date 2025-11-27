# Mediator 自动批量（AutoBatching）

轻量、贴合 Polly 的 Mediator 端自动合批能力。默认关闭，启用后按“数量阈值/时间窗口”合并同类型请求，以减少调度开销、提升吞吐，并与现有 Polly 弹性策略（超时/舱壁/断路/重试）无缝集成。

## 为什么在 Mediator 合批？
- 面向 CPU/Handler 的聚合，适合热点请求、数据库/缓存访问等。与“传输层批量”互补（网络视角）。
- 不改变既有 Handler 即可启用，进一步可通过“按键分片”和“真批处理 Handler”获得更大收益。

## 启用
```csharp
services.AddCatga()
    .UseMediatorAutoBatching(o =>
    {
        o.EnableAutoBatching = true;
        o.MaxBatchSize = 64;                   // 数量阈值
        o.BatchTimeout = TimeSpan.FromMilliseconds(25); // 时间阈值（已内置±10%抖动，降低集群同步刷新）
        o.MaxQueueLength = 5000;               // 队列上限（溢出时丢弃最旧项）
        o.ShardIdleTtl = TimeSpan.FromMinutes(2); // 分片空闲回收 TTL
        o.MaxShards = 2048;                    // 分片上限（高基数保护）
    })
    // 可选：启用“每请求类型”的覆盖配置与 Key 选择器（源生成自动注册）
    .UseMediatorAutoBatchingProfilesFromAssembly()
    .UseResilience(); // 建议显式开启 Polly（批次刷新会始终包裹在 Mediator resilience 下）
```

## 关键选项
- MaxBatchSize：批次最大条数。数值越大吞吐越高、尾延时可能增大。
- BatchTimeout：时间窗口；窗口到期即刷新（已加±10%抖动，降低跨节点同步）。
- MaxQueueLength：单分片排队上限；超限时丢弃最旧请求并返回失败。
- ShardIdleTtl：分片在“空闲且无积压”状态下的回收 TTL。
- MaxShards：最大分片数；超过上限将优先淘汰“空闲且最久未使用”的分片。
- FlushDegree：批内小并发度。0 表示串行（默认），>0 表示在同一批内以该并发度并行调用 Handler（仍受 Polly 舱壁/超时保护）。

## 分片（可选）：IBatchKeyProvider
为避免“热点+长尾”场景下的头阻塞，可按 Key 分片：
```csharp
public record GetOrders(string TenantId, int Page) : IRequest<PagedOrders>, IBatchKeyProvider
{
    public string? BatchKey => TenantId; // 同租户请求进入同一分片
}
```
- 未实现该接口 → 使用单一分片（零成本）。
- 高基数 Key 场景建议：合理设置 `ShardIdleTtl` 与 `MaxShards`。

## 每类型配置（源生成）
为减少样板代码并获得“按请求类型”覆盖配置与 Key 选择器，提供 Attribute + Source Generator：

```csharp
using Catga.Abstractions;

[BatchOptions(MaxBatchSize = 128, BatchTimeoutMs = 15, MaxQueueLength = 8000, FlushDegree = 4)]
[BatchKey(nameof(TenantId))]
public record GetOrders(string TenantId, int Page) : IRequest<PagedOrders>;
```

- `[BatchOptions]`：仅填写需要覆盖的字段，未填写的使用全局 `UseMediatorAutoBatching(...)` 中的值。
- `[BatchKey]`：指定分片键属性，源生成会生成零反射的强类型选择器。
- DI 中调用 `.UseMediatorAutoBatchingProfilesFromAssembly()` 即可完成注册。

注意：全局 `EnableAutoBatching` 为“总开关”。若全局关闭，即使有每类型覆盖也不会启用该类型的批量（但不会引入额外开销）。

## 观测
当启用 `WithTracing(true)` 时输出以下指标：
- catga.mediator.batch.size（Histogram）
- catga.mediator.batch.queue_length（Histogram）
- catga.mediator.batch.flush.duration（Histogram）
- catga.mediator.batch.overflow（Counter）

建议在 Grafana 关注：P50/P95 批次大小、刷新耗时、溢出速率。

## 刷新与弹性
- 每次批次刷新在 `IResiliencePipelineProvider.ExecuteMediatorAsync` 下执行（遵循 Polly 舱壁/超时/断路/重试）。
- 批内默认逐条调用 `next()`；当 `FlushDegree > 0` 时，同一批次内以该并发度限制并行处理（建议谨慎启用，并结合 Polly 舱壁配置评估）。
- 如需向量化收益，可新增：
  - IBatchRequestHandler<TRequest,TResponse>.HandleBatchAsync(IReadOnlyList<TRequest>)（可选，未实现则回退逐条）。

## 尺寸与调优建议
- 低延时优先：`MaxBatchSize` 小、`BatchTimeout` 短；适度提高并发能力（Polly 舱壁配置）。
- 吞吐优先：`MaxBatchSize` 稍大、`BatchTimeout` 适中；确保队列上限足够并关注 `overflow`。
- 集群部署：默认抖动已开启；不同节点批次窗口错峰，降低突发同步刷新风险。

## 溢出策略
- 当分片队列超限时，丢弃最旧项并返回失败（计数：`catga.mediator.batch.overflow`）。
- 生产建议：结合业务 SLO 调整 `MaxQueueLength/BatchTimeout/MaxBatchSize`，必要时增加“最新丢弃/阻塞等待（带超时）”策略（未来版本可选）。

## 已知限制
- 默认批内串行；可通过可选批处理 Handler 获得更多 I/O 向量化收益（规划中）。

## 常见问题
- 与传输层批量冲突吗？
  - 否。Mediator 聚焦 Handler/CPU 维度；Transport 聚焦网络/消息维度，二者互补。
- 需要启用 Polly 吗？
  - 建议。即使未显式启用，批次刷新也会在 Provider 下执行；完整弹性策略仍推荐 `UseResilience()`。
