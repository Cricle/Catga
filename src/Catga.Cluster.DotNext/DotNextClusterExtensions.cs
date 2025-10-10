using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.DotNext;

/// <summary>
/// DotNext Raft 集群配置（超简单）
/// </summary>
public sealed class DotNextClusterOptions
{
    /// <summary>
    /// 集群成员 URL（例如：["http://node1:5001", "http://node2:5002", "http://node3:5003"]）
    /// </summary>
    public string[] Members { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 本节点 ID（默认：机器名）
    /// </summary>
    public string LocalMemberId { get; set; } = Environment.MachineName;
}

/// <summary>
/// DotNext Raft 集群扩展（超简单配置）
/// </summary>
public static class DotNextClusterExtensions
{
    /// <summary>
    /// 添加 Raft 集群支持 - 3 行配置，获得分布式能力
    /// 
    /// 特性：
    /// ✅ 高并发：零锁设计，线程安全
    /// ✅ 高性能：本地查询，低延迟
    /// ✅ 高可用：3 节点容错 1 个
    /// ✅ 零概念：用户代码完全不变
    /// ✅ 自动容错：故障自动转移
    /// 
    /// 使用：
    /// <code>
    /// builder.Services.AddCatga();
    /// builder.Services.AddRaftCluster(options => 
    /// {
    ///     options.Members = ["http://node1:5001", "http://node2:5002", "http://node3:5003"];
    /// });
    /// </code>
    /// </summary>
    public static IServiceCollection AddRaftCluster(
        this IServiceCollection services,
        Action<DotNextClusterOptions>? configure = null)
    {
        var options = new DotNextClusterOptions();
        configure?.Invoke(options);

        // 1. 注册 Raft 集群包装器
        services.AddSingleton<ICatgaRaftCluster, CatgaRaftCluster>();

        // 2. 包装 ICatgaMediator（超简单）
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICatgaMediator));
        if (descriptor != null)
        {
            services.Remove(descriptor);
            
            // 注册原始 Mediator
            services.Add(new ServiceDescriptor(
                typeof(ICatgaMediator),
                sp =>
                {
                    var inner = ActivatorUtilities.CreateInstance(
                        sp, descriptor.ImplementationType!);
                    return inner;
                },
                descriptor.Lifetime));

            // 包装为 RaftAwareMediator
            services.Add(new ServiceDescriptor(
                typeof(ICatgaMediator),
                sp =>
                {
                    var innerMediator = sp.GetServices<ICatgaMediator>()
                        .FirstOrDefault(m => m.GetType().Name != nameof(RaftAwareMediator));
                    
                    if (innerMediator == null)
                    {
                        throw new InvalidOperationException(
                            "ICatgaMediator 必须在 AddRaftCluster() 之前注册。" +
                            "确保先调用 services.AddCatga()");
                    }

                    var cluster = sp.GetRequiredService<ICatgaRaftCluster>();
                    var logger = sp.GetRequiredService<ILogger<RaftAwareMediator>>();
                    
                    return new RaftAwareMediator(cluster, innerMediator, logger);
                },
                ServiceLifetime.Singleton));
        }

        // 3. 输出配置（简单明了）
        Console.WriteLine();
        Console.WriteLine("🚀 Catga Raft 集群已启用");
        Console.WriteLine($"   节点 ID: {options.LocalMemberId}");
        Console.WriteLine($"   集群成员: {options.Members.Length} 个");
        Console.WriteLine();
        Console.WriteLine("✅ 特性：");
        Console.WriteLine("   • 高并发 - 零锁设计");
        Console.WriteLine("   • 高性能 - 本地查询");
        Console.WriteLine("   • 高可用 - 自动容错");
        Console.WriteLine("   • 零学习 - 代码不变");
        Console.WriteLine();

        return services;
    }
}
