using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Catga.ServiceDiscovery;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Catga.ServiceDiscovery.Kubernetes;

/// <summary>
/// Kubernetes 服务发现实现（使用 K8s API）
/// </summary>
public class KubernetesServiceDiscovery : IServiceDiscovery
{
    private readonly IKubernetes _kubernetes;
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<KubernetesServiceDiscovery> _logger;
    private readonly string _namespace;

    public KubernetesServiceDiscovery(
        IKubernetes kubernetes,
        string @namespace = "default",
        ILoadBalancer? loadBalancer = null,
        ILogger<KubernetesServiceDiscovery>? logger = null)
    {
        _kubernetes = kubernetes;
        _namespace = @namespace;
        _loadBalancer = loadBalancer ?? new RoundRobinLoadBalancer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<KubernetesServiceDiscovery>.Instance;
    }

    public Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default)
    {
        // K8s 服务发现不需要注册（由 K8s Service 管理）
        _logger.LogWarning("Kubernetes service discovery does not support registration");
        return Task.CompletedTask;
    }

    public Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // K8s 服务发现不需要注销
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 获取 Service
            var service = await _kubernetes.CoreV1.ReadNamespacedServiceAsync(
                serviceName,
                _namespace,
                cancellationToken: cancellationToken);

            if (service?.Spec?.ClusterIP == null)
            {
                _logger.LogWarning("Service {ServiceName} has no ClusterIP", serviceName);
                return Array.Empty<ServiceInstance>();
            }

            // 获取 Endpoints
            var endpoints = await _kubernetes.CoreV1.ReadNamespacedEndpointsAsync(
                serviceName,
                _namespace,
                cancellationToken: cancellationToken);

            var instances = new List<ServiceInstance>();

            if (endpoints?.Subsets != null)
            {
                foreach (var subset in endpoints.Subsets)
                {
                    if (subset.Addresses != null && subset.Ports != null)
                    {
                        foreach (var address in subset.Addresses)
                        {
                            foreach (var port in subset.Ports)
                            {
                                var metadata = new Dictionary<string, string>
                                {
                                    ["pod"] = address.TargetRef?.Name ?? "unknown",
                                    ["namespace"] = _namespace,
                                    ["protocol"] = port.Protocol ?? "TCP"
                                };

                                var instance = new ServiceInstance(
                                    $"{serviceName}-{address.Ip}-{port.Port}",
                                    serviceName,
                                    address.Ip,
                                    port.Port,
                                    metadata)
                                {
                                    IsHealthy = true
                                };

                                instances.Add(instance);
                            }
                        }
                    }
                }
            }

            _logger.LogDebug("Found {Count} instances for service {ServiceName} in namespace {Namespace}",
                instances.Count, serviceName, _namespace);

            return instances;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service instances for {ServiceName}", serviceName);
            return Array.Empty<ServiceInstance>();
        }
    }

    public async Task<ServiceInstance?> GetServiceInstanceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var instances = await GetServiceInstancesAsync(serviceName, cancellationToken);
        return _loadBalancer.SelectInstance(instances);
    }

    public Task SendHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // K8s 不需要心跳（由 K8s 健康检查管理）
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ServiceChangeEvent>();

        // 启动 Watch 任务 - 使用 LongRunning 避免线程池阻塞
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                var watchTask = _kubernetes.CoreV1.ListNamespacedEndpointsWithHttpMessagesAsync(
                    namespaceParameter: _namespace,
                    fieldSelector: $"metadata.name={serviceName}",
                    watch: true,
                    cancellationToken: cancellationToken);

                await foreach (var (type, item) in watchTask.WatchAsync<V1Endpoints, V1EndpointsList>(
                    onError: ex => _logger.LogError(ex, "Error watching service {ServiceName}", serviceName),
                    cancellationToken: cancellationToken))
                {
                    var changeType = type switch
                    {
                        WatchEventType.Added => ServiceChangeType.Registered,
                        WatchEventType.Deleted => ServiceChangeType.Deregistered,
                        WatchEventType.Modified => ServiceChangeType.HealthChanged,
                        _ => ServiceChangeType.HealthChanged
                    };

                    // 解析 Endpoints 并生成事件
                    if (item?.Subsets != null)
                    {
                        foreach (var subset in item.Subsets)
                        {
                            if (subset.Addresses != null && subset.Ports != null)
                            {
                                foreach (var address in subset.Addresses)
                                {
                                    foreach (var port in subset.Ports)
                                    {
                                        var instance = new ServiceInstance(
                                            $"{serviceName}-{address.Ip}-{port.Port}",
                                            serviceName,
                                            address.Ip,
                                            port.Port);

                                        await channel.Writer.WriteAsync(
                                            new ServiceChangeEvent(changeType, instance),
                                            cancellationToken);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watch loop for service {ServiceName}", serviceName);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // 读取事件
        await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return change;
        }
    }
}

