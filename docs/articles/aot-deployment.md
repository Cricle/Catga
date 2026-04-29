# Catga AOT 部署说明

这篇文章回答的是：Catga 做 Native AOT 时，配置重点是什么，真正需要避开的坑是什么。

如果你要完整发布步骤，直接看：

- [Native AOT 发布指南](../deployment/native-aot-publishing.md)
- [序列化 AOT 指南](../aot/serialization-aot-guide.md)

## 先看结论

Catga 当前 AOT 路线很明确：

- `AddCatga()` 后优先使用 `UseMemoryPack()`
- 不要依赖运行时反射扫描来补消息序列化
- transport / persistence 选型要和 AOT 目标一起考虑
- 发布前必须把 trimming / AOT warning 清干净

## 最小可用配置

### 项目文件

下面是一个最小示例，按当前仓库常用写法使用 `net10.0`：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <PublishTrimmed>true</PublishTrimmed>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

### 服务注册

```csharp
using Catga.DependencyInjection;

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

builder.Services.AddInMemoryTransport();
```

这条路径的意义是：

- serializer 用 `MemoryPack`
- persistence 用 `InMemory`
- transport 用 `InMemory`
- 先把 AOT 发布链路跑通，再切生产 broker

## 为什么默认推荐 MemoryPack

对 Catga 来说，AOT 里最重要的一层不是 broker，而是 serializer。

当前推荐 `UseMemoryPack()` 的原因：

- 当前 analyzer 就是按这条路径引导
- 不需要额外引入一套 JSON 反射序列化兜底
- AOT 场景下更稳
- 生产文档和示例也统一围绕它展开

示例：

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack();
```

如果不用 MemoryPack，就必须自己显式注册 `IMessageSerializer`，并且自己承担 AOT 兼容性验证。

## AOT 下的 broker 选择建议

AOT 不会直接决定你必须用哪个 broker，但会影响你该从哪条配置路径开始。

### 最容易先跑通

- `UseInMemory() + AddInMemoryTransport()`

### 默认生产答案

- `UseRedis(...) + AddRedisTransport(...)`

### 企业标准 broker

- `UseRedis(...) + AddRabbitMqTransport(...)`

### 一体化 broker 世界

- `UseNats(...) + AddNatsTransport(...)`

详细选型看：

- [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)

## 发布前检查项

发布前至少确认这几件事：

1. `AddCatga()` 后已经接上 `UseMemoryPack()` 或自定义 `IMessageSerializer`
2. 没有依赖运行时反射扫描来做消息注册
3. trim / AOT warning 已逐条确认
4. 当前 transport / persistence 组合已经在 publish 产物里跑通过

## 常见问题

### 1. 还在用旧 JSON 示例

当前仓库不应该再把 `AddJsonMessageSerializer()` 当成默认推荐路径。

### 2. AOT 跑通了，但生产配置没跑通

这通常不是 AOT 本身的问题，而是 transport、persistence、hosting 没按当前 API 接齐。

### 3. 把 AOT 和 benchmark 混成一件事

AOT 关注的是：

- 发布方式
- 启动时行为
- trimming / reflection 风险

benchmark 关注的是：

- 运行时调用成本
- 分配
- 吞吐

这两个问题不要混着看。

## 继续往下看

- 完整发布步骤：看 [Native AOT 发布指南](../deployment/native-aot-publishing.md)
- 序列化限制：看 [序列化 AOT 指南](../aot/serialization-aot-guide.md)
- 配置入口：看 [配置指南](./configuration.md)
