using Catga.ServiceDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServiceDiscoveryDemo;

/// <summary>
/// YARP 和 Kubernetes 服务发现示例
/// </summary>
public static class YarpKubernetesExample
{
    /// <summary>
    /// YARP 服务发现示例
    /// </summary>
    public static async Task RunYarpExample()
    {
        Console.WriteLine("🔧 YARP 服务发现示例\n");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // 注意：实际使用时需要先配置 YARP
        // services.AddReverseProxy().LoadFromConfig(...);
        // services.AddYarpServiceDiscovery();

        Console.WriteLine("  YARP 服务发现特点:");
        Console.WriteLine("  • 从 YARP 配置读取服务信息");
        Console.WriteLine("  • 与 YARP 反向代理共享配置");
        Console.WriteLine("  • 支持配置热重载");
        Console.WriteLine("  • 适合已使用 YARP 的应用\n");

        Console.WriteLine("  配置示例 (appsettings.json):");
        Console.WriteLine(@"  {
    ""ReverseProxy"": {
      ""Clusters"": {
        ""order-service"": {
          ""Destinations"": {
            ""primary"": { ""Address"": ""http://localhost:5001"" },
            ""secondary"": { ""Address"": ""http://localhost:5002"" }
          }
        }
      }
    }
  }");

        Console.WriteLine("\n  使用代码:");
        Console.WriteLine(@"  services.AddReverseProxy()
      .LoadFromConfig(configuration.GetSection(""ReverseProxy""));
  services.AddYarpServiceDiscovery();
  
  var instance = await discovery.GetServiceInstanceAsync(""order-service"");
  // 返回: localhost:5001 或 localhost:5002（负载均衡）");

        Console.WriteLine();
    }

    /// <summary>
    /// Kubernetes 服务发现示例
    /// </summary>
    public static async Task RunKubernetesExample()
    {
        Console.WriteLine("☸️  Kubernetes API 服务发现示例\n");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // 注意：实际使用时需要在 K8s 环境中
        // services.AddKubernetesServiceDiscoveryInCluster(namespace: "default");

        Console.WriteLine("  Kubernetes 服务发现特点:");
        Console.WriteLine("  • 使用原生 K8s API");
        Console.WriteLine("  • 实时监听 Endpoints 变化");
        Console.WriteLine("  • 自动发现所有 Pod IP");
        Console.WriteLine("  • 支持多命名空间");
        Console.WriteLine("  • Kubernetes 环境首选方案\n");

        Console.WriteLine("  使用方式 1 - 集群内模式（Pod 中运行）:");
        Console.WriteLine(@"  services.AddKubernetesServiceDiscoveryInCluster(
      namespace: ""default"");");

        Console.WriteLine("\n  使用方式 2 - 本地开发（指定 KubeConfig）:");
        Console.WriteLine(@"  services.AddKubernetesServiceDiscovery(options =>
  {
      options.Namespace = ""production"";
      options.KubeConfigPath = ""~/.kube/config"";
  });");

        Console.WriteLine("\n  RBAC 权限配置 (k8s-rbac.yaml):");
        Console.WriteLine(@"  apiVersion: rbac.authorization.k8s.io/v1
  kind: Role
  metadata:
    name: service-discovery
    namespace: default
  rules:
  - apiGroups: [""""]
    resources: [""services"", ""endpoints""]
    verbs: [""get"", ""list"", ""watch""]");

        Console.WriteLine("\n  使用示例:");
        Console.WriteLine(@"  var discovery = provider.GetRequiredService<IServiceDiscovery>();
  
  // 获取所有实例（所有 Pod）
  var instances = await discovery.GetServiceInstancesAsync(""order-service"");
  foreach (var instance in instances)
  {
      Console.WriteLine($""Pod: {instance.Host}:{instance.Port}"");
  }
  
  // 监听服务变化（Pod 启动/停止）
  await foreach (var change in discovery.WatchServiceAsync(""order-service""))
  {
      Console.WriteLine($""Service changed: {change.ChangeType}"");
  }");

        Console.WriteLine();
    }

    /// <summary>
    /// 对比不同实现
    /// </summary>
    public static void CompareImplementations()
    {
        Console.WriteLine("📊 服务发现实现对比\n");

        var data = new[]
        {
            new { Name = "Memory", Scene = "开发/测试", Dep = "无", Health = "❌", Dynamic = "✅", Recommend = "⭐⭐⭐" },
            new { Name = "DNS", Scene = "K8s基础", Dep = "无", Health = "❌", Dynamic = "❌", Recommend = "⭐⭐⭐" },
            new { Name = "Consul", Scene = "企业级", Dep = "Consul", Health = "✅", Dynamic = "✅", Recommend = "⭐⭐⭐⭐" },
            new { Name = "YARP", Scene = "YARP用户", Dep = "YARP", Health = "✅", Dynamic = "✅", Recommend = "⭐⭐⭐⭐" },
            new { Name = "K8s API", Scene = "Kubernetes", Dep = "K8s", Health = "✅", Dynamic = "✅", Recommend = "⭐⭐⭐⭐⭐" }
        };

        Console.WriteLine($"{"实现",-10} {"适用场景",-15} {"依赖",-10} {"健康检查",-10} {"动态配置",-10} {"推荐度",-10}");
        Console.WriteLine(new string('-', 80));

        foreach (var item in data)
        {
            Console.WriteLine($"{item.Name,-10} {item.Scene,-15} {item.Dep,-10} {item.Health,-10} {item.Dynamic,-10} {item.Recommend,-10}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 选择建议
    /// </summary>
    public static void ShowRecommendations()
    {
        Console.WriteLine("💡 选择建议\n");

        Console.WriteLine("1. 本地开发 → Memory");
        Console.WriteLine("   特点: 快速启动，零依赖");
        Console.WriteLine("   代码: services.AddMemoryServiceDiscovery();\n");

        Console.WriteLine("2. Kubernetes (简单) → DNS");
        Console.WriteLine("   特点: 无需额外配置，但功能有限");
        Console.WriteLine("   代码: services.AddDnsServiceDiscovery();\n");

        Console.WriteLine("3. Kubernetes (推荐) → K8s API ⭐");
        Console.WriteLine("   特点: 完整功能，实时监听，自动发现");
        Console.WriteLine("   代码: services.AddKubernetesServiceDiscoveryInCluster();\n");

        Console.WriteLine("4. 使用 YARP → YARP");
        Console.WriteLine("   特点: 与 YARP 配置统一管理");
        Console.WriteLine("   代码: services.AddYarpServiceDiscovery();\n");

        Console.WriteLine("5. 混合云/企业级 → Consul");
        Console.WriteLine("   特点: 跨平台，功能完整，生产验证");
        Console.WriteLine("   代码: services.AddConsulServiceDiscovery();\n");
    }
}

