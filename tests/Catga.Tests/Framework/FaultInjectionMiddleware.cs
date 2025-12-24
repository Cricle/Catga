namespace Catga.Tests.Framework;

/// <summary>
/// 故障注入中间件
/// 用于在测试中模拟各种故障场景
/// </summary>
public class FaultInjectionMiddleware
{
    /// <summary>
    /// 故障类型枚举
    /// </summary>
    public enum FaultType
    {
        /// <summary>网络超时</summary>
        NetworkTimeout,
        
        /// <summary>连接失败</summary>
        ConnectionFailure,
        
        /// <summary>序列化错误</summary>
        SerializationError,
        
        /// <summary>版本冲突</summary>
        VersionConflict,
        
        /// <summary>资源耗尽</summary>
        ResourceExhaustion,
        
        /// <summary>数据损坏</summary>
        DataCorruption,
        
        /// <summary>慢操作（延迟）</summary>
        SlowOperation,
        
        /// <summary>部分失败</summary>
        PartialFailure
    }

    /// <summary>
    /// 故障配置
    /// </summary>
    public class FaultConfiguration
    {
        /// <summary>故障类型</summary>
        public FaultType Type { get; set; }
        
        /// <summary>故障概率 (0.0 - 1.0)</summary>
        public double Probability { get; set; } = 0.1;
        
        /// <summary>延迟时间（毫秒）- 用于 SlowOperation</summary>
        public int DelayMs { get; set; } = 1000;
        
        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>自定义异常消息</summary>
        public string? CustomMessage { get; set; }
    }

    private readonly Dictionary<FaultType, FaultConfiguration> _faultConfigurations = new();
    private readonly Random _random = new();
    private readonly object _lock = new();

    /// <summary>
    /// 注入故障
    /// </summary>
    /// <param name="faultType">故障类型</param>
    /// <param name="probability">故障概率 (0.0 - 1.0)</param>
    /// <param name="delayMs">延迟时间（毫秒）- 用于 SlowOperation</param>
    public void InjectFault(FaultType faultType, double probability = 0.1, int delayMs = 1000)
    {
        lock (_lock)
        {
            _faultConfigurations[faultType] = new FaultConfiguration
            {
                Type = faultType,
                Probability = probability,
                DelayMs = delayMs,
                Enabled = true
            };
        }
    }

    /// <summary>
    /// 注入故障（使用配置对象）
    /// </summary>
    public void InjectFault(FaultConfiguration configuration)
    {
        lock (_lock)
        {
            _faultConfigurations[configuration.Type] = configuration;
        }
    }

    /// <summary>
    /// 清除指定类型的故障
    /// </summary>
    public void ClearFault(FaultType faultType)
    {
        lock (_lock)
        {
            _faultConfigurations.Remove(faultType);
        }
    }

    /// <summary>
    /// 清除所有故障
    /// </summary>
    public void ClearAllFaults()
    {
        lock (_lock)
        {
            _faultConfigurations.Clear();
        }
    }

    /// <summary>
    /// 检查是否应该触发故障
    /// </summary>
    /// <param name="faultType">故障类型</param>
    /// <returns>如果应该触发故障则抛出异常</returns>
    public async Task CheckAndThrowAsync(FaultType faultType)
    {
        FaultConfiguration? config;
        lock (_lock)
        {
            if (!_faultConfigurations.TryGetValue(faultType, out config) || !config.Enabled)
                return;
        }

        // 检查是否应该触发故障
        if (_random.NextDouble() >= config.Probability)
            return;

        // 根据故障类型执行相应的操作
        switch (faultType)
        {
            case FaultType.NetworkTimeout:
                await Task.Delay(config.DelayMs);
                throw new TimeoutException(config.CustomMessage ?? "Network timeout injected by FaultInjectionMiddleware");

            case FaultType.ConnectionFailure:
                throw new InvalidOperationException(config.CustomMessage ?? "Connection failure injected by FaultInjectionMiddleware");

            case FaultType.SerializationError:
                throw new InvalidOperationException(config.CustomMessage ?? "Serialization error injected by FaultInjectionMiddleware");

            case FaultType.VersionConflict:
                throw new InvalidOperationException(config.CustomMessage ?? "Version conflict injected by FaultInjectionMiddleware");

            case FaultType.ResourceExhaustion:
                throw new OutOfMemoryException(config.CustomMessage ?? "Resource exhaustion injected by FaultInjectionMiddleware");

            case FaultType.DataCorruption:
                throw new InvalidDataException(config.CustomMessage ?? "Data corruption injected by FaultInjectionMiddleware");

            case FaultType.SlowOperation:
                await Task.Delay(config.DelayMs);
                break;

            case FaultType.PartialFailure:
                throw new InvalidOperationException(config.CustomMessage ?? "Partial failure injected by FaultInjectionMiddleware");

            default:
                throw new ArgumentException($"Unknown fault type: {faultType}");
        }
    }

    /// <summary>
    /// 检查是否应该触发故障（同步版本）
    /// </summary>
    public void CheckAndThrow(FaultType faultType)
    {
        CheckAndThrowAsync(faultType).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 获取故障统计信息
    /// </summary>
    public FaultStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new FaultStatistics
            {
                TotalFaults = _faultConfigurations.Count,
                EnabledFaults = _faultConfigurations.Count(kvp => kvp.Value.Enabled),
                FaultTypes = _faultConfigurations.Keys.ToList()
            };
        }
    }

    /// <summary>
    /// 故障统计信息
    /// </summary>
    public class FaultStatistics
    {
        /// <summary>总故障数</summary>
        public int TotalFaults { get; set; }
        
        /// <summary>已启用的故障数</summary>
        public int EnabledFaults { get; set; }
        
        /// <summary>故障类型列表</summary>
        public List<FaultType> FaultTypes { get; set; } = new();
    }
}

/// <summary>
/// 故障注入扩展方法
/// </summary>
public static class FaultInjectionExtensions
{
    /// <summary>
    /// 在操作中注入故障
    /// </summary>
    public static async Task<T> WithFaultInjection<T>(
        this Task<T> operation,
        FaultInjectionMiddleware middleware,
        FaultInjectionMiddleware.FaultType faultType)
    {
        await middleware.CheckAndThrowAsync(faultType);
        return await operation;
    }

    /// <summary>
    /// 在操作中注入故障
    /// </summary>
    public static async Task WithFaultInjection(
        this Task operation,
        FaultInjectionMiddleware middleware,
        FaultInjectionMiddleware.FaultType faultType)
    {
        await middleware.CheckAndThrowAsync(faultType);
        await operation;
    }

    /// <summary>
    /// 执行操作并自动重试（遇到故障时）
    /// </summary>
    public static async Task<T> WithRetryOnFault<T>(
        this Func<Task<T>> operation,
        int maxRetries = 3,
        int delayMs = 100)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception) when (attempt < maxRetries)
            {
                attempt++;
                await Task.Delay(delayMs * attempt); // Exponential backoff
            }
        }
    }

    /// <summary>
    /// 执行操作并自动重试（遇到故障时）
    /// </summary>
    public static async Task WithRetryOnFault(
        this Func<Task> operation,
        int maxRetries = 3,
        int delayMs = 100)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception) when (attempt < maxRetries)
            {
                attempt++;
                await Task.Delay(delayMs * attempt); // Exponential backoff
            }
        }
    }
}
