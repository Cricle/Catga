using System.Runtime.CompilerServices;

namespace Catga.Streaming;

/// <summary>
/// 流处理抽象接口（平台无关）
/// </summary>
/// <typeparam name="TInput">输入消息类型</typeparam>
/// <typeparam name="TOutput">输出消息类型</typeparam>
public interface IStreamProcessor<TInput, TOutput>
{
    /// <summary>
    /// 流式处理消息
    /// </summary>
    IAsyncEnumerable<TOutput> ProcessAsync(
        IAsyncEnumerable<TInput> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 流处理管道构建器
/// </summary>
public interface IStreamPipeline<T>
{
    /// <summary>
    /// 过滤消息
    /// </summary>
    IStreamPipeline<T> Where(Func<T, bool> predicate);

    /// <summary>
    /// 转换消息
    /// </summary>
    IStreamPipeline<TResult> Select<TResult>(Func<T, TResult> selector);

    /// <summary>
    /// 异步转换消息
    /// </summary>
    IStreamPipeline<TResult> SelectAsync<TResult>(Func<T, Task<TResult>> selector);

    /// <summary>
    /// 批处理
    /// </summary>
    IStreamPipeline<IReadOnlyList<T>> Batch(int batchSize, TimeSpan? timeout = null);

    /// <summary>
    /// 窗口聚合
    /// </summary>
    IStreamPipeline<IReadOnlyList<T>> Window(TimeSpan windowSize);

    /// <summary>
    /// 去重
    /// </summary>
    IStreamPipeline<T> Distinct<TKey>(Func<T, TKey> keySelector) where TKey : notnull;

    /// <summary>
    /// 限流
    /// </summary>
    IStreamPipeline<T> Throttle(int maxItemsPerSecond);

    /// <summary>
    /// 延迟处理
    /// </summary>
    IStreamPipeline<T> Delay(TimeSpan delay);

    /// <summary>
    /// 并行处理
    /// </summary>
    IStreamPipeline<T> Parallel(int degreeOfParallelism);

    /// <summary>
    /// 执行副作用操作
    /// </summary>
    IStreamPipeline<T> Do(Action<T> action);

    /// <summary>
    /// 执行异步副作用操作
    /// </summary>
    IStreamPipeline<T> DoAsync(Func<T, Task> action);

    /// <summary>
    /// 执行管道并返回结果流
    /// </summary>
    IAsyncEnumerable<T> ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 流处理源
/// </summary>
public interface IStreamSource<T>
{
    /// <summary>
    /// 获取数据流
    /// </summary>
    IAsyncEnumerable<T> GetStreamAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 流处理汇
/// </summary>
public interface IStreamSink<T>
{
    /// <summary>
    /// 写入数据流
    /// </summary>
    Task WriteStreamAsync(IAsyncEnumerable<T> stream, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于通道的流源
/// </summary>
public class ChannelStreamSource<T> : IStreamSource<T>
{
    private readonly System.Threading.Channels.ChannelReader<T> _reader;

    public ChannelStreamSource(System.Threading.Channels.ChannelReader<T> reader)
    {
        _reader = reader;
    }

    public async IAsyncEnumerable<T> GetStreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

/// <summary>
/// 基于通道的流汇
/// </summary>
public class ChannelStreamSink<T> : IStreamSink<T>
{
    private readonly System.Threading.Channels.ChannelWriter<T> _writer;

    public ChannelStreamSink(System.Threading.Channels.ChannelWriter<T> writer)
    {
        _writer = writer;
    }

    public async Task WriteStreamAsync(IAsyncEnumerable<T> stream, CancellationToken cancellationToken = default)
    {
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            await _writer.WriteAsync(item, cancellationToken);
        }
    }
}

/// <summary>
/// 流处理器工厂
/// </summary>
public static class StreamProcessor
{
    /// <summary>
    /// 创建流处理管道
    /// </summary>
    public static IStreamPipeline<T> From<T>(IAsyncEnumerable<T> source)
    {
        return new StreamPipeline<T>(source);
    }

    /// <summary>
    /// 创建流处理管道（从源）
    /// </summary>
    public static IStreamPipeline<T> From<T>(IStreamSource<T> source, CancellationToken cancellationToken = default)
    {
        return new StreamPipeline<T>(source.GetStreamAsync(cancellationToken));
    }
}

