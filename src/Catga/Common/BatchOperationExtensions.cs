using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>
/// Extensions for batch operations with common patterns
/// Reduces code duplication for batch processing scenarios
/// </summary>
internal static class BatchOperationExtensions
{
    private const int DefaultArrayPoolThreshold = 16;

    /// <summary>
    /// Execute batch operations in parallel with ArrayPool optimization
    /// </summary>
    /// <typeparam name="T">Item type</typeparam>
    /// <param name="items">Items to process</param>
    /// <param name="action">Async action to execute for each item</param>
    /// <param name="arrayPoolThreshold">Threshold for using ArrayPool</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task ExecuteBatchAsync<T>(
        this IReadOnlyList<T> items,
        Func<T, Task> action,
        int arrayPoolThreshold = DefaultArrayPoolThreshold)
    {
        if (items == null || items.Count == 0)
            return;

        // Fast path: Single item
        if (items.Count == 1)
        {
            await action(items[0]).ConfigureAwait(false);
            return;
        }

        // Batch processing with ArrayPool
        using var rentedTasks = ArrayPoolHelper.RentOrAllocate<Task>(items.Count, arrayPoolThreshold);
        var tasks = rentedTasks.Array;

        // Start all tasks
        for (int i = 0; i < items.Count; i++)
        {
            tasks[i] = action(items[i]);
        }

        // Wait for all tasks (use exact count, not rented array length)
        await Task.WhenAll(rentedTasks.AsSpan().ToArray()).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute batch operations and collect results
    /// </summary>
    /// <typeparam name="TSource">Source item type</typeparam>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <param name="items">Items to process</param>
    /// <param name="action">Async action to execute for each item</param>
    /// <param name="arrayPoolThreshold">Threshold for using ArrayPool</param>
    /// <returns>Results array</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IReadOnlyList<TResult>> ExecuteBatchWithResultsAsync<TSource, TResult>(
        this IReadOnlyList<TSource> items,
        Func<TSource, ValueTask<TResult>> action,
        int arrayPoolThreshold = DefaultArrayPoolThreshold)
    {
        if (items == null || items.Count == 0)
            return Array.Empty<TResult>();

        // Fast path: Single item
        if (items.Count == 1)
        {
            var result = await action(items[0]).ConfigureAwait(false);
            return new[] { result };
        }

        // Batch processing
        using var rentedResults = ArrayPoolHelper.RentOrAllocate<TResult>(items.Count, arrayPoolThreshold);
        using var rentedTasks = ArrayPoolHelper.RentOrAllocate<ValueTask<TResult>>(items.Count, arrayPoolThreshold);

        var results = rentedResults.Array;
        var tasks = rentedTasks.Array;

        // Start all tasks
        for (int i = 0; i < items.Count; i++)
        {
            tasks[i] = action(items[i]);
        }

        // Wait for all tasks
        for (int i = 0; i < items.Count; i++)
        {
            results[i] = await tasks[i].ConfigureAwait(false);
        }

        // Copy to final array (exact size)
        var finalResults = new TResult[items.Count];
        Array.Copy(results, finalResults, items.Count);
        return finalResults;
    }
}

