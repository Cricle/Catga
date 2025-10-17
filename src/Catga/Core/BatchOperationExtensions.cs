using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>Batch operation extensions with ArrayPool optimization</summary>
public static class BatchOperationExtensions
{
    private const int DefaultArrayPoolThreshold = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task ExecuteBatchAsync<T>(this IReadOnlyList<T> items, Func<T, Task> action, int arrayPoolThreshold = DefaultArrayPoolThreshold)
    {
        if (items == null || items.Count == 0) return;

        if (items.Count == 1)
        {
            await action(items[0]).ConfigureAwait(false);
            return;
        }

        using var rentedTasks = ArrayPoolHelper.RentOrAllocate<Task>(items.Count, arrayPoolThreshold);
        var tasks = rentedTasks.Array;

        for (int i = 0; i < items.Count; i++)
            tasks[i] = action(items[i]);

        // Zero-allocation: use exact-sized array or ArraySegment
        if (tasks.Length == items.Count)
        {
            await Task.WhenAll((IEnumerable<Task>)tasks).ConfigureAwait(false);
        }
        else
        {
            await Task.WhenAll((IEnumerable<Task>)new ArraySegment<Task>(tasks, 0, items.Count)).ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IReadOnlyList<TResult>> ExecuteBatchWithResultsAsync<TSource, TResult>(this IReadOnlyList<TSource> items, Func<TSource, ValueTask<TResult>> action, int arrayPoolThreshold = DefaultArrayPoolThreshold)
    {
        if (items == null || items.Count == 0) return Array.Empty<TResult>();

        if (items.Count == 1)
        {
            var result = await action(items[0]).ConfigureAwait(false);
            return new[] { result };
        }

        var rentedResults = ArrayPoolHelper.RentOrAllocate<TResult>(items.Count, arrayPoolThreshold);
        using var rentedTasks = ArrayPoolHelper.RentOrAllocate<ValueTask<TResult>>(items.Count, arrayPoolThreshold);

        var results = rentedResults.Array;
        var tasks = rentedTasks.Array;

        for (int i = 0; i < items.Count; i++)
            tasks[i] = action(items[i]);

        for (int i = 0; i < items.Count; i++)
            results[i] = await tasks[i].ConfigureAwait(false);

        // ✅ 优化：避免不必要的拷贝
        if (results.Length == items.Count)
        {
            // 完美匹配，从 pool 中分离并直接返回
            return rentedResults.Detach();
        }
        else
        {
            // 需要精确大小（租赁的数组更大）
            var finalResults = new TResult[items.Count];
            results.AsSpan(0, items.Count).CopyTo(finalResults);  // ✅ 使用 Span.CopyTo（更快，可能触发 SIMD）
            rentedResults.Dispose();
            return finalResults;
        }
    }
}

