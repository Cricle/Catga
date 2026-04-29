# Catga.AspNetCore

`Catga.AspNetCore` 提供的是 ASP.NET Core 集成辅助能力，不是一个完整的“HTTP 自动总线平台”。

按当前仓库实现，最稳定的部分主要是：

- `CatgaResult<T> -> IResult` 映射
- 相关 Swagger metadata 扩展
- CorrelationId middleware
- Endpoint error handling middleware

## 安装

```bash
dotnet add package Catga.AspNetCore
```

## 推荐用法

当前推荐的主路径是：

- 业务消息仍走 `ICatgaMediator`
- ASP.NET Core 只负责 HTTP 入口
- 用 `ToHttpResult()` 做结果映射

### 基本接法

```csharp
using Catga.DependencyInjection;
using Catga.AspNetCore.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

builder.Services.AddInMemoryTransport();

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

app.UseCorrelationId();
app.UseEndpointErrorHandling();

app.MapHealthChecks("/health");

app.MapPost("/api/orders", async (
    CreateOrder command,
    ICatgaMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(command, ct);
    return result.ToHttpResult(201);
});

app.Run();
```

## `ToHttpResult()` 映射

`CatgaResult<T>` 可以直接映射成 ASP.NET Core `IResult`：

```csharp
app.MapGet("/api/orders/{id}", async (
    string id,
    ICatgaMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.SendAsync<GetOrder, OrderDto?>(new GetOrder(id), ct);
    return result.ToHttpResult();
});
```

默认错误码映射基于 `ErrorCode`：

- `ValidationFailed` -> `422`
- `NotFound` -> `404`
- `Conflict` -> `409`
- `Unauthorized` -> `401`
- `Forbidden` -> `403`
- `PersistenceFailed / LockFailed / TransportFailed` -> `503`

## 中间件

### CorrelationId

```csharp
app.UseCorrelationId();
```

适合：

- 希望 HTTP 请求带入 correlation id
- 需要和 Catga tracing / logging 口径对齐

### Endpoint Error Handling

```csharp
app.UseEndpointErrorHandling();
```

默认错误序列化器是纯文本格式；如果你需要自定义输出，可以注册：

```csharp
builder.Services.AddErrorResponseSerializer<MyErrorResponseSerializer>();
```

## Swagger Metadata

如果你使用 Minimal API，可以给路由补 Catga 语义元数据：

```csharp
app.MapPost("/api/orders", async (
    CreateOrder command,
    ICatgaMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(command, ct);
    return result.ToHttpResult(201);
})
.WithCatgaCommandMetadata<CreateOrder, OrderResult>();
```

可用扩展：

- `WithCatgaCommandMetadata<TCommand, TResponse>()`
- `WithCatgaQueryMetadata<TQuery, TResponse>()`
- `WithCatgaEventMetadata<TEvent>()`

## 关于 `UseCatga()`

当前仓库里 `UseCatga()` 只是一个非常轻量的 application builder 扩展点，不应该再被当成“自动挂完整 dashboard / diagnostics endpoint”的稳定能力来理解。

如果你要健康检查，请显式使用：

```csharp
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

app.MapHealthChecks("/health");
```

## 关于自动 HTTP 端点注册

仓库里存在 endpoint 生成相关文档和占位文件，但按当前代码状态，不建议把它当成主推荐路径。

当前更稳妥的做法仍然是：

- 手写 Minimal API / Controller 入口
- 注入 `ICatgaMediator`
- 使用 `ToHttpResult()`

## AOT 说明

如果你追求最稳的 AOT 路线：

- `Catga` 核心层优先 `UseMemoryPack()`
- ASP.NET Core 入口尽量走显式 Minimal API
- 不要把“自动 HTTP 端点生成”当作当前默认路径

## 相关文档

- `docs/articles/getting-started.md`
- `docs/articles/configuration.md`
- `docs/guides/hosting-configuration.md`
