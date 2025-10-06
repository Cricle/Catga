using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Streaming;

/// <summary>
/// 流处理管道实现
/// </summary>
internal class StreamPipeline<T> : IStreamPipeline<T>
{
    private readonly IAsyncEnumerable<T> _source;

    public StreamPipeline(IAsyncEnumerable<T> source)
    {
        _source = source;
    }

    public IStreamPipeline<T> Where(Func<T, bool> predicate)
    {
        return new StreamPipeline<T>(WhereImpl(predicate));
    }

    private async IAsyncEnumerable<T> WhereImpl(
        Func<T, bool> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    public IStreamPipeline<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        return new StreamPipeline<TResult>(SelectImpl(selector));
    }

    private async IAsyncEnumerable<TResult> SelectImpl<TResult>(
        Func<T, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            yield return selector(item);
        }
    }

    public IStreamPipeline<TResult> SelectAsync<TResult>(Func<T, Task<TResult>> selector)
    {
        return new StreamPipeline<TResult>(SelectAsyncImpl(selector));
    }

    private async IAsyncEnumerable<TResult> SelectAsyncImpl<TResult>(
        Func<T, Task<TResult>> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            yield return await selector(item);
        }
    }

    public IStreamPipeline<IReadOnlyList<T>> Batch(int batchSize, TimeSpan? timeout = null)
    {
        return new StreamPipeline<IReadOnlyList<T>>(BatchImpl(batchSize, timeout));
    }

    private async IAsyncEnumerable<IReadOnlyList<T>> BatchImpl(
        int batchSize,
        TimeSpan? timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var batch = new List<T>(batchSize);
        var lastEmitTime = DateTime.UtcNow;

        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            batch.Add(item);

            var shouldEmit = batch.Count >= batchSize ||
                             (timeout.HasValue && DateTime.UtcNow - lastEmitTime >= timeout.Value);

            if (shouldEmit)
            {
                yield return batch.ToArray();
                batch.Clear();
                lastEmitTime = DateTime.UtcNow;
            }
        }

        // 发送剩余的批次
        if (batch.Count > 0)
        {
            yield return batch.ToArray();
        }
    }

    public IStreamPipeline<IReadOnlyList<T>> Window(TimeSpan windowSize)
    {
        return new StreamPipeline<IReadOnlyList<T>>(WindowImpl(windowSize));
    }

    private async IAsyncEnumerable<IReadOnlyList<T>> WindowImpl(
        TimeSpan windowSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var window = new List<T>();
        var windowStart = DateTime.UtcNow;

        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            var now = DateTime.UtcNow;

            // 如果窗口时间到了，发送窗口数据
            if (now - windowStart >= windowSize)
            {
                if (window.Count > 0)
                {
                    yield return window.ToArray();
                    window.Clear();
                }
                windowStart = now;
            }

            window.Add(item);
        }

        // 发送剩余的窗口数据
        if (window.Count > 0)
        {
            yield return window.ToArray();
        }
    }

    public IStreamPipeline<T> Distinct<TKey>(Func<T, TKey> keySelector) where TKey : notnull
    {
        return new StreamPipeline<T>(DistinctImpl(keySelector));
    }

    private async IAsyncEnumerable<T> DistinctImpl<TKey>(
        Func<T, TKey> keySelector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where TKey : notnull
    {
        var seen = new HashSet<TKey>();

        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            var key = keySelector(item);
            if (seen.Add(key))
            {
                yield return item;
            }
        }
    }

    public IStreamPipeline<T> Throttle(int maxItemsPerSecond)
    {
        return new StreamPipeline<T>(ThrottleImpl(maxItemsPerSecond));
    }

    private async IAsyncEnumerable<T> ThrottleImpl(
        int maxItemsPerSecond,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var minInterval = TimeSpan.FromSeconds(1.0 / maxItemsPerSecond);
        var lastEmitTime = DateTime.MinValue;

        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            var now = DateTime.UtcNow;
            var timeSinceLastEmit = now - lastEmitTime;

            if (timeSinceLastEmit < minInterval)
            {
                await Task.Delay(minInterval - timeSinceLastEmit, cancellationToken);
            }

            lastEmitTime = DateTime.UtcNow;
            yield return item;
        }
    }

    public IStreamPipeline<T> Delay(TimeSpan delay)
    {
        return new StreamPipeline<T>(DelayImpl(delay));
    }

    private async IAsyncEnumerable<T> DelayImpl(
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            await Task.Delay(delay, cancellationToken);
            yield return item;
        }
    }

    public IStreamPipeline<T> Parallel(int degreeOfParallelism)
    {
        return new StreamPipeline<T>(ParallelImpl(degreeOfParallelism));
    }

    private async IAsyncEnumerable<T> ParallelImpl(
        int degreeOfParallelism,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new ConcurrentQueue<T>();
        var outputChannel = System.Threading.Channels.Channel.CreateUnbounded<T>();

        // 启动并行消费者
        var tasks = Enumerable.Range(0, degreeOfParallelism)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var item in _source.WithCancellation(cancellationToken))
                {
                    await outputChannel.Writer.WriteAsync(item, cancellationToken);
                }
            }, cancellationToken))
            .ToArray();

        // 等待所有任务完成后关闭通道
        _ = Task.Run(async () =>
        {
            await Task.WhenAll(tasks);
            outputChannel.Writer.Complete();
        }, cancellationToken);

        // 读取结果
        await foreach (var item in outputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public IStreamPipeline<T> Do(Action<T> action)
    {
        return new StreamPipeline<T>(DoImpl(action));
    }

    private async IAsyncEnumerable<T> DoImpl(
        Action<T> action,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            action(item);
            yield return item;
        }
    }

    public IStreamPipeline<T> DoAsync(Func<T, Task> action)
    {
        return new StreamPipeline<T>(DoAsyncImpl(action));
    }

    private async IAsyncEnumerable<T> DoAsyncImpl(
        Func<T, Task> action,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source.WithCancellation(cancellationToken))
        {
            await action(item);
            yield return item;
        }
    }

    public IAsyncEnumerable<T> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return _source;
    }
}

