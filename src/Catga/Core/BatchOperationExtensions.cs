using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Batch operation extensions</summary>
public static class BatchOperationExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task ExecuteBatchAsync<T>(this IReadOnlyList<T> items, Func<T, Task> action)
    {
        if (items == null || items.Count == 0) return;

        if (items.Count == 1)
        {
            await action(items[0]).ConfigureAwait(false);
            return;
        }

        var tasks = new Task[items.Count];

        for (var i = 0; i < items.Count; i++)
            tasks[i] = action(items[i]);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IReadOnlyList<TResult>> ExecuteBatchWithResultsAsync<TSource, TResult>(
        this IReadOnlyList<TSource> items, 
        Func<TSource, ValueTask<TResult>> action)
    {
        if (items == null || items.Count == 0) return Array.Empty<TResult>();

        if (items.Count == 1)
        {
            var result = await action(items[0]).ConfigureAwait(false);
            return [result];
        }

        var tasks = new ValueTask<TResult>[items.Count];
        var results = new TResult[items.Count];

        for (var i = 0; i < items.Count; i++)
            tasks[i] = action(items[i]);

        for (var i = 0; i < items.Count; i++)
            results[i] = await tasks[i].ConfigureAwait(false);

        return results;
    }
}
