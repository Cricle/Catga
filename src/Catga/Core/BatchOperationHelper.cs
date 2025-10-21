using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>
/// Helper for batch parallel operations with chunking to prevent thread pool starvation (DRY principle)
/// </summary>
public static class BatchOperationHelper
{
    /// <summary>Default chunk size for large batches</summary>
    public const int DefaultChunkSize = 100;

    /// <summary>
    /// Execute batch async operations in parallel with automatic chunking for large batches.
    /// Prevents thread pool starvation by processing items in chunks.
    /// </summary>
    /// <param name="items">Items to process</param>
    /// <param name="operation">Operation to execute on each item</param>
    /// <param name="chunkSize">Chunk size for processing (default: 100). Set to 0 to disable chunking.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int chunkSize = DefaultChunkSize)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        // Fast path: if already ICollection, use Count directly
        if (items is ICollection<T> collection)
        {
            if (collection.Count == 0)
                return Task.CompletedTask;

            // Small batch or chunking disabled: execute all at once
            if (collection.Count <= chunkSize || chunkSize <= 0)
            {
                var tasks = new Task[collection.Count];
                int index = 0;
                foreach (var item in collection)
                    tasks[index++] = operation(item);

                return Task.WhenAll(tasks);
            }

            // Large batch: use chunking
            return ExecuteChunkedAsync(collection, operation, chunkSize);
        }

        // Slow path: materialize to list
        return ExecuteBatchSlowPathAsync(items, operation, chunkSize);
    }

    private static async Task ExecuteBatchSlowPathAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int chunkSize)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        // Small batch or chunking disabled: execute all at once
        if (itemList.Count <= chunkSize || chunkSize <= 0)
        {
            var tasks = new Task[itemList.Count];
            for (int i = 0; i < itemList.Count; i++)
                tasks[i] = operation(itemList[i]);

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return;
        }

        // Large batch: use chunking
        await ExecuteChunkedAsync(itemList, operation, chunkSize).ConfigureAwait(false);
    }

    private static async Task ExecuteChunkedAsync<T>(
        ICollection<T> items,
        Func<T, Task> operation,
        int chunkSize)
    {
        var itemList = items as IList<T> ?? items.ToList();
        var totalCount = itemList.Count;

        // Process in chunks to avoid thread pool starvation
        for (int i = 0; i < totalCount; i += chunkSize)
        {
            var end = Math.Min(i + chunkSize, totalCount);
            var chunkTasks = new Task[end - i];

            for (int j = i; j < end; j++)
                chunkTasks[j - i] = operation(itemList[j]);

            await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Execute batch async operations with parameter in parallel with automatic chunking.
    /// </summary>
    /// <param name="items">Items to process</param>
    /// <param name="parameter">Parameter to pass to each operation</param>
    /// <param name="operation">Operation to execute on each item</param>
    /// <param name="chunkSize">Chunk size for processing (default: 100). Set to 0 to disable chunking.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        int chunkSize = DefaultChunkSize)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        if (items is ICollection<T> collection)
        {
            if (collection.Count == 0)
                return Task.CompletedTask;

            // Small batch or chunking disabled: execute all at once
            if (collection.Count <= chunkSize || chunkSize <= 0)
            {
                var tasks = new Task[collection.Count];
                int index = 0;
                foreach (var item in collection)
                    tasks[index++] = operation(item, parameter);

                return Task.WhenAll(tasks);
            }

            // Large batch: use chunking
            return ExecuteChunkedAsync(collection, parameter, operation, chunkSize);
        }

        return ExecuteBatchSlowPathAsync(items, parameter, operation, chunkSize);
    }

    private static async Task ExecuteBatchSlowPathAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        int chunkSize)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        // Small batch or chunking disabled: execute all at once
        if (itemList.Count <= chunkSize || chunkSize <= 0)
        {
            var tasks = new Task[itemList.Count];
            for (int i = 0; i < itemList.Count; i++)
                tasks[i] = operation(itemList[i], parameter);

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return;
        }

        // Large batch: use chunking
        await ExecuteChunkedAsync(itemList, parameter, operation, chunkSize).ConfigureAwait(false);
    }

    private static async Task ExecuteChunkedAsync<T, TParam>(
        ICollection<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        int chunkSize)
    {
        var itemList = items as IList<T> ?? items.ToList();
        var totalCount = itemList.Count;

        // Process in chunks to avoid thread pool starvation
        for (int i = 0; i < totalCount; i += chunkSize)
        {
            var end = Math.Min(i + chunkSize, totalCount);
            var chunkTasks = new Task[end - i];

            for (int j = i; j < end; j++)
                chunkTasks[j - i] = operation(itemList[j], parameter);

            await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Execute batch operations with concurrency control using SemaphoreSlim.
    /// Useful when you need to limit concurrent operations regardless of batch size.
    /// Optimized to avoid List resizing by pre-allocating when count is known.
    /// </summary>
    /// <param name="items">Items to process</param>
    /// <param name="operation">Operation to execute on each item</param>
    /// <param name="maxConcurrency">Maximum number of concurrent operations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task ExecuteConcurrentBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int maxConcurrency,
        CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than 0");

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        
        // Optimize: pre-allocate List if count is known to avoid resizing
        var tasks = items is ICollection<T> collection 
            ? new List<Task>(collection.Count)
            : new List<Task>();

        foreach (var item in items)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await operation(item).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
