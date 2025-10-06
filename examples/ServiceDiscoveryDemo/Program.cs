using Catga.DependencyInjection;
using Catga.ServiceDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================
// Catga 服务发现示例
// ============================================================

Console.WriteLine("🔍 Catga 服务发现示例\n");

// ============================================================
// 示例 1: 内存服务发现（单机部署）
// ============================================================
Console.WriteLine("📝 示例 1: 内存服务发现");

var services1 = new ServiceCollection();
services1.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services1.AddMemoryServiceDiscovery();

var provider1 = services1.BuildServiceProvider();
var discovery1 = provider1.GetRequiredService<IServiceDiscovery>();

// 注册多个服务实例
await discovery1.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 5001
});

await discovery1.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 5002
});

await discovery1.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "order-service",
    Host = "localhost",
    Port = 5003
});

// 获取所有实例
var instances = await discovery1.GetServiceInstancesAsync("order-service");
Console.WriteLine($"  发现 {instances.Count} 个服务实例:");
foreach (var instance in instances)
{
    Console.WriteLine($"    - {instance.ServiceId}: {instance.Address}");
}

// 负载均衡获取实例
Console.WriteLine("\n  负载均衡测试（轮询）:");
for (int i = 0; i < 5; i++)
{
    var instance = await discovery1.GetServiceInstanceAsync("order-service");
    Console.WriteLine($"    请求 {i + 1}: {instance?.Address}");
}

Console.WriteLine();

// ============================================================
// 示例 2: DNS 服务发现（Kubernetes）
// ============================================================
Console.WriteLine("🌐 示例 2: DNS 服务发现");

var services2 = new ServiceCollection();
services2.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services2.AddDnsServiceDiscovery(options =>
{
    // 配置 Kubernetes Service DNS
    options.MapService("order-service", "order-service.default.svc.cluster.local", 8080);
    options.MapService("payment-service", "payment-service.default.svc.cluster.local", 8080);
});

var provider2 = services2.BuildServiceProvider();
var discovery2 = provider2.GetRequiredService<IServiceDiscovery>();

Console.WriteLine("  DNS 服务发现配置完成");
Console.WriteLine("  - order-service -> order-service.default.svc.cluster.local:8080");
Console.WriteLine("  - payment-service -> payment-service.default.svc.cluster.local:8080");

Console.WriteLine();

// ============================================================
// 示例 3: 自动服务注册（后台服务）
// ============================================================
Console.WriteLine("🔄 示例 3: 自动服务注册");

var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddMemoryServiceDiscovery();
        services.AddServiceRegistration(new ServiceRegistrationOptions
        {
            ServiceName = "my-api",
            Host = "localhost",
            Port = 8080,
            HealthCheckUrl = "http://localhost:8080/health",
            HealthCheckInterval = TimeSpan.FromSeconds(5),
            DeregisterOnShutdown = true
        });
    })
    .Build();

Console.WriteLine("  启动服务注册...");

var cts = new CancellationTokenSource();
var hostTask = host.StartAsync(cts.Token);

// 等待一段时间
await Task.Delay(2000);

Console.WriteLine("  服务已注册，心跳正常");

// 停止服务
await host.StopAsync();
Console.WriteLine("  服务已注销");

Console.WriteLine();

// ============================================================
// 示例 4: 服务监听（实时感知服务变化）
// ============================================================
Console.WriteLine("👀 示例 4: 服务监听");

var services4 = new ServiceCollection();
services4.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services4.AddMemoryServiceDiscovery();

var provider4 = services4.BuildServiceProvider();
var discovery4 = provider4.GetRequiredService<IServiceDiscovery>();

// 启动监听任务
var watchCts = new CancellationTokenSource();
var watchTask = Task.Run(async () =>
{
    await foreach (var change in discovery4.WatchServiceAsync("user-service", watchCts.Token))
    {
        Console.WriteLine($"  🔔 服务变化: {change.ChangeType} - {change.Instance.ServiceId}");
    }
}, watchCts.Token);

// 模拟服务注册和注销
await Task.Delay(500);
await discovery4.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "user-service",
    Host = "localhost",
    Port = 6001
});

await Task.Delay(500);
await discovery4.RegisterAsync(new ServiceRegistrationOptions
{
    ServiceName = "user-service",
    Host = "localhost",
    Port = 6002
});

await Task.Delay(1000);

// 停止监听
watchCts.Cancel();
try { await watchTask; } catch { /* 忽略取消异常 */ }

Console.WriteLine();

// ============================================================
// 示例 5: 负载均衡策略对比
// ============================================================
Console.WriteLine("⚖️ 示例 5: 负载均衡策略对比");

// 轮询
var roundRobin = new RoundRobinLoadBalancer();
Console.WriteLine("  轮询负载均衡:");
var testInstances = new[]
{
    new ServiceInstance("1", "test", "host1", 8001),
    new ServiceInstance("2", "test", "host2", 8002),
    new ServiceInstance("3", "test", "host3", 8003)
};

for (int i = 0; i < 6; i++)
{
    var selected = roundRobin.SelectInstance(testInstances);
    Console.WriteLine($"    请求 {i + 1}: {selected?.Address}");
}

// 随机
var random = new RandomLoadBalancer();
Console.WriteLine("\n  随机负载均衡:");
for (int i = 0; i < 6; i++)
{
    var selected = random.SelectInstance(testInstances);
    Console.WriteLine($"    请求 {i + 1}: {selected?.Address}");
}

Console.WriteLine("\n✅ 所有示例完成！");
Console.WriteLine("\n💡 提示:");
Console.WriteLine("   - 内存服务发现: 适合单机和测试");
Console.WriteLine("   - DNS 服务发现: 适合 Kubernetes");
Console.WriteLine("   - Consul 服务发现: 适合复杂分布式环境（需要 Catga.ServiceDiscovery.Consul 包）");

