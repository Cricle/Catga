# 🔍 服务发现与注册

Catga 提供平台无关的服务发现抽象，支持多种实现方式。

---

## 📋 目录

- [核心概念](#核心概念)
- [快速开始](#快速开始)
- [实现方式](#实现方式)
- [负载均衡](#负载均衡)
- [服务监听](#服务监听)
- [最佳实践](#最佳实践)

---

## 核心概念

### 什么是服务发现？

在分布式系统中，服务实例的位置是动态的（IP、端口会变化）。服务发现解决了"如何找到服务实例"的问题。

### Catga 的设计原则

✅ **平台无关** - 不绑定任何特定平台
✅ **适量抽象** - 简单易用，不过度设计
✅ **多种实现** - 内存、DNS、Consul 等
✅ **自动切换** - 无需修改代码即可切换实现

---

## 快速开始

### 1. 内存服务发现（单机/测试）

```csharp
using Catga.DependencyInjection;
using Catga.ServiceDiscovery;

var services = new ServiceCollection();

// 添加内存服务发现
services.AddMemoryServiceDiscovery();

var provider = services.BuildServiceProvider();
var discovery = provider.GetRequiredService<IServiceDiscovery>();

// 注册服务
await discovery.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 5001
});

// 发现服务
var instance = await discovery.GetServiceInstanceAsync("order-service");
Console.WriteLine($"服务地址: {instance.Address}");
```

### 2. DNS 服务发现（Kubernetes）

```csharp
services.AddDnsServiceDiscovery(options =>
{
    // Kubernetes Service DNS
    options.MapService("order-service", "order-service.default.svc.cluster.local", 8080);
    options.MapService("payment-service", "payment-service.default.svc.cluster.local", 8080);
});
```

### 3. Consul 服务发现（企业级）

```csharp
// 需要安装 Catga.ServiceDiscovery.Consul 包
using Catga.ServiceDiscovery.Consul;

services.AddConsulServiceDiscovery(options =>
{
    options.ConsulAddress = "http://consul:8500";
    options.Token = "your-token"; // 可选
});
```

---

## 实现方式

### 对比表

| 实现方式 | 适用场景 | 优点 | 缺点 |
|---------|---------|------|------|
| **内存** | 单机、测试 | 简单、无依赖 | 不适合分布式 |
| **DNS** | Kubernetes | 云原生、自动更新 | 功能有限 |
| **Consul** | 复杂分布式 | 功能完整、健康检查 | 需要额外部署 |

### 内存服务发现

**特点**:
- ✅ 零依赖
- ✅ 适合单机和测试
- ✅ 支持服务监听
- ❌ 不支持跨进程

**使用场景**:
- 本地开发
- 单元测试
- 单体应用

**示例**:

```csharp
services.AddMemoryServiceDiscovery();

var discovery = provider.GetRequiredService<IServiceDiscovery>();

// 注册多个实例
await discovery.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 5001,
    Metadata = new Dictionary<string, string>
    {
        ["version"] = "1.0.0",
        ["region"] = "us-west"
    }
});

// 获取所有实例
var instances = await discovery.GetServiceInstancesAsync("order-service");
```

### DNS 服务发现

**特点**:
- ✅ Kubernetes 原生支持
- ✅ 自动负载均衡
- ✅ 无需额外组件
- ❌ 不支持健康检查
- ❌ 不支持元数据

**使用场景**:
- Kubernetes 部署
- Docker Compose
- 简单微服务

**Kubernetes 示例**:

```yaml
# deployment.yaml
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
  ports:
    - port: 8080
      targetPort: 8080
```

```csharp
// C# 配置
services.AddDnsServiceDiscovery(options =>
{
    options.MapService("order-service", "order-service.default.svc.cluster.local", 8080);
});

// 使用
var discovery = provider.GetRequiredService<IServiceDiscovery>();
var instances = await discovery.GetServiceInstancesAsync("order-service");
// 返回所有 Pod 的 IP 地址
```

### Consul 服务发现

**特点**:
- ✅ 完整的服务注册和发现
- ✅ 健康检查（HTTP、TCP、TTL）
- ✅ 服务元数据
- ✅ 实时监听
- ✅ 多数据中心
- ❌ 需要部署 Consul

**使用场景**:
- 企业级微服务
- 混合云部署
- 需要健康检查

**安装**:

```bash
dotnet add package Catga.ServiceDiscovery.Consul
```

**配置**:

```csharp
using Catga.ServiceDiscovery.Consul;

services.AddConsulServiceDiscovery(options =>
{
    options.ConsulAddress = "http://consul:8500";
    options.Token = "your-consul-token"; // ACL Token（可选）
    options.Datacenter = "dc1";          // 数据中心（可选）
});

// 自动注册
services.AddServiceRegistration(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "10.0.1.5",
    Port = 8080,
    HealthCheckUrl = "http://10.0.1.5:8080/health",
    HealthCheckInterval = TimeSpan.FromSeconds(10),
    HealthCheckTimeout = TimeSpan.FromSeconds(5),
    DeregisterOnShutdown = true
});
```

**Docker Compose 示例**:

```yaml
version: '3.8'

services:
  consul:
    image: consul:latest
    ports:
      - "8500:8500"
    command: agent -server -ui -bootstrap-expect=1 -client=0.0.0.0

  order-service:
    image: myapp/order-service
    environment:
      - CONSUL_ADDRESS=http://consul:8500
      - SERVICE_NAME=order-service
      - SERVICE_PORT=8080
```

---

## 负载均衡

### 内置策略

#### 1. 轮询（Round Robin）

```csharp
services.AddMemoryServiceDiscovery(); // 默认使用轮询

var instance = await discovery.GetServiceInstanceAsync("order-service");
// 依次返回: Instance1 -> Instance2 -> Instance3 -> Instance1 ...
```

#### 2. 随机（Random）

```csharp
services.TryAddSingleton<ILoadBalancer, RandomLoadBalancer>();
services.AddMemoryServiceDiscovery();

var instance = await discovery.GetServiceInstanceAsync("order-service");
// 随机返回一个实例
```

### 自定义负载均衡

```csharp
public class WeightedLoadBalancer : ILoadBalancer
{
    public ServiceInstance? SelectInstance(IReadOnlyList<ServiceInstance> instances)
    {
        // 基于权重选择
        var weights = instances.Select(i =>
        {
            var weight = i.Metadata?.GetValueOrDefault("weight", "1") ?? "1";
            return int.Parse(weight);
        }).ToArray();

        // 加权随机算法
        var totalWeight = weights.Sum();
        var random = Random.Shared.Next(totalWeight);

        var cumulative = 0;
        for (int i = 0; i < instances.Count; i++)
        {
            cumulative += weights[i];
            if (random < cumulative)
                return instances[i];
        }

        return instances[0];
    }
}

// 注册
services.TryAddSingleton<ILoadBalancer, WeightedLoadBalancer>();
```

---

## 服务监听

监听服务实例的注册和注销事件。

### 示例

```csharp
var discovery = provider.GetRequiredService<IServiceDiscovery>();

await foreach (var change in discovery.WatchServiceAsync("order-service", cancellationToken))
{
    switch (change.ChangeType)
    {
        case ServiceChangeType.Registered:
            Console.WriteLine($"新实例注册: {change.Instance.Address}");
            break;

        case ServiceChangeType.Deregistered:
            Console.WriteLine($"实例注销: {change.Instance.Address}");
            break;

        case ServiceChangeType.HealthChanged:
            Console.WriteLine($"健康状态变化: {change.Instance.IsHealthy}");
            break;
    }
}
```

### 实现热重载

```csharp
public class DynamicServiceClient
{
    private readonly IServiceDiscovery _discovery;
    private List<ServiceInstance> _instances = new();

    public DynamicServiceClient(IServiceDiscovery discovery)
    {
        _discovery = discovery;
        _ = WatchServiceChangesAsync();
    }

    private async Task WatchServiceChangesAsync()
    {
        await foreach (var change in _discovery.WatchServiceAsync("order-service"))
        {
            // 重新加载实例列表
            _instances = (await _discovery.GetServiceInstancesAsync("order-service")).ToList();
            Console.WriteLine($"服务实例已更新: {_instances.Count} 个");
        }
    }

    public async Task<HttpResponseMessage> CallServiceAsync(string path)
    {
        var instance = _instances[Random.Shared.Next(_instances.Count)];
        var client = new HttpClient { BaseAddress = new Uri($"http://{instance.Address}") };
        return await client.GetAsync(path);
    }
}
```

---

## 自动注册

使用后台服务自动注册和心跳。

### 配置

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加服务发现
builder.Services.AddMemoryServiceDiscovery();

// 自动注册
builder.Services.AddServiceRegistration(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 8080,
    HealthCheckUrl = "http://localhost:8080/health",
    HealthCheckInterval = TimeSpan.FromSeconds(10),
    DeregisterOnShutdown = true // 关闭时自动注销
});

var app = builder.Build();
app.Run();
```

### 生命周期

1. **启动时** - 自动注册到服务发现
2. **运行时** - 定期发送心跳
3. **关闭时** - 自动注销（如果 `DeregisterOnShutdown = true`）

---

## 最佳实践

### 1. 选择合适的实现

```
单机应用    -> MemoryServiceDiscovery
Kubernetes  -> DnsServiceDiscovery
其他场景    -> ConsulServiceDiscovery
```

### 2. 健康检查

```csharp
// 添加健康检查端点
app.MapHealthChecks("/health");

// 注册时指定健康检查
services.AddServiceRegistration(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 8080,
    HealthCheckUrl = "http://localhost:8080/health",
    HealthCheckInterval = TimeSpan.FromSeconds(10)
});
```

### 3. 服务元数据

```csharp
await discovery.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 5001,
    Metadata = new Dictionary<string, string>
    {
        ["version"] = "1.0.0",
        ["region"] = "us-west",
        ["weight"] = "10"
    }
});
```

### 4. 错误处理

```csharp
public async Task<ServiceInstance?> GetServiceWithRetryAsync(string serviceName)
{
    for (int i = 0; i < 3; i++)
    {
        try
        {
            var instance = await _discovery.GetServiceInstanceAsync(serviceName);
            if (instance != null)
                return instance;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get service instance, retry {Attempt}/3", i + 1);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    return null;
}
```

### 5. 与 NATS 集成

```csharp
// 使用服务发现获取 NATS 地址
var natsInstance = await discovery.GetServiceInstanceAsync("nats");
if (natsInstance != null)
{
    services.AddNatsCatga($"nats://{natsInstance.Address}");
}
```

---

## 对比：Catga vs 其他框架

| 特性 | Catga | Consul 直接 | Eureka |
|------|-------|------------|--------|
| **平台无关** | ✅ | ❌ | ❌ |
| **多种实现** | ✅ | ❌ | ❌ |
| **易于切换** | ✅ | ❌ | ❌ |
| **无依赖** | ✅ (内存模式) | ❌ | ❌ |
| **Kubernetes** | ✅ | ✅ | ❌ |
| **健康检查** | ✅ (Consul) | ✅ | ✅ |

---

## 总结

- ✅ **平台无关** - 统一抽象，多种实现
- ✅ **渐进式** - 从内存到 DNS 到 Consul
- ✅ **易于测试** - 内存实现零依赖
- ✅ **生产就绪** - Consul 完整功能

**建议**:
- 开发/测试 → MemoryServiceDiscovery
- Kubernetes → DnsServiceDiscovery
- 企业级 → ConsulServiceDiscovery

