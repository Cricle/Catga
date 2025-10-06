// ⚠️ 实验性功能 - API 可能在未来版本中变化
// 建议使用 Microsoft.Extensions.Configuration 代替

namespace Catga.Configuration;

/// <summary>
/// 配置中心抽象接口
/// <para>⚠️ 实验性功能 - API 可能变化</para>
/// <para>推荐使用 Microsoft.Extensions.Configuration</para>
/// </summary>
public interface IConfigurationCenter
{
    /// <summary>
    /// 获取配置值
    /// </summary>
    Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取强类型配置
    /// </summary>
    Task<T?> GetConfigAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 设置配置值
    /// </summary>
    Task SetConfigAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除配置
    /// </summary>
    Task DeleteConfigAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听配置变化
    /// </summary>
    IAsyncEnumerable<ConfigurationChangeEvent> WatchConfigAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// 配置变化事件
/// </summary>
public record ConfigurationChangeEvent(
    string Key,
    string? OldValue,
    string? NewValue,
    ConfigurationChangeType ChangeType,
    DateTime Timestamp)
{
    public ConfigurationChangeEvent(string key, string? oldValue, string? newValue, ConfigurationChangeType changeType)
        : this(key, oldValue, newValue, changeType, DateTime.UtcNow)
    {
    }
}

/// <summary>
/// 配置变化类型
/// </summary>
public enum ConfigurationChangeType
{
    /// <summary>
    /// 新增
    /// </summary>
    Added,

    /// <summary>
    /// 修改
    /// </summary>
    Modified,

    /// <summary>
    /// 删除
    /// </summary>
    Deleted
}

/// <summary>
/// 内存配置中心实现（用于测试）
/// <para>⚠️ 实验性功能</para>
/// </summary>
public class MemoryConfigurationCenter : IConfigurationCenter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _configs = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.Channels.Channel<ConfigurationChangeEvent>> _watchers = new();

    public Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken = default)
    {
        _configs.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public async Task<T?> GetConfigAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var value = await GetConfigAsync(key, cancellationToken);
        if (value == null)
            return null;

        return System.Text.Json.JsonSerializer.Deserialize<T>(value);
    }

    public Task SetConfigAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var oldValue = _configs.TryGetValue(key, out var old) ? old : null;
        _configs[key] = value;

        var changeType = oldValue == null ? ConfigurationChangeType.Added : ConfigurationChangeType.Modified;
        NotifyWatchers(key, new ConfigurationChangeEvent(key, oldValue, value, changeType));

        return Task.CompletedTask;
    }

    public Task DeleteConfigAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_configs.TryRemove(key, out var oldValue))
        {
            NotifyWatchers(key, new ConfigurationChangeEvent(key, oldValue, null, ConfigurationChangeType.Deleted));
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ConfigurationChangeEvent> WatchConfigAsync(
        string key,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<ConfigurationChangeEvent>();
        _watchers[key] = channel;

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
        finally
        {
            _watchers.TryRemove(key, out _);
        }
    }

    private void NotifyWatchers(string key, ConfigurationChangeEvent change)
    {
        if (_watchers.TryGetValue(key, out var channel))
        {
            channel.Writer.TryWrite(change);
        }

        // 通知通配符监听者
        if (_watchers.TryGetValue("*", out var wildcardChannel))
        {
            wildcardChannel.Writer.TryWrite(change);
        }
    }
}
