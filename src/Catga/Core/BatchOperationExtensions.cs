using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Batch operation extensions for IReadOnlyList</summary>
public static class BatchOperationExtensions
{
    /// <summary>
    /// Execute batch async operations in parallel (optimized for single-item)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T>(this IReadOnlyList<T> items, Func<T, Task> action)
    {
        if (items == null || items.Count == 0)
            return Task.CompletedTask;

        // Fast path: single item (avoid array allocation)
        if (items.Count == 1)
            return action(items[0]);

        // Delegate to BatchOperationHelper for parallel execution
        return BatchOperationHelper.ExecuteBatchAsync(items, action);
    }

    /// <summary>
    /// Execute batch async operations with results in parallel (optimized for single-item)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IReadOnlyList<TResult>> ExecuteBatchWithResultsAsync<TSource, TResult>(
        this IReadOnlyList<TSource> items,
        Func<TSource, ValueTask<TResult>> action)
    {
        if (items == null || items.Count == 0)
            return Array.Empty<TResult>();

        // Fast path: single item (avoid array allocation)
        if (items.Count == 1)
        {
            var result = await action(items[0]).ConfigureAwait(false);
            return new[] { result };
        }

        // Parallel execution
        var tasks = new ValueTask<TResult>[items.Count];
        for (var i = 0; i < items.Count; i++)
            tasks[i] = action(items[i]);

        var results = new TResult[items.Count];
        for (var i = 0; i < items.Count; i++)
            results[i] = await tasks[i].ConfigureAwait(false);

        return results;
    }
}
