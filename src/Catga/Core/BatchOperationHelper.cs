using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>
/// Helper for batch parallel operations (DRY principle)
/// </summary>
public static class BatchOperationHelper
{
    /// <summary>
    /// Execute batch async operations in parallel
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        // Fast path: if already ICollection, use Count directly
        if (items is ICollection<T> collection)
        {
            if (collection.Count == 0)
                return Task.CompletedTask;

            var tasks = new Task[collection.Count];
            int index = 0;
            foreach (var item in collection)
                tasks[index++] = operation(item);

            return Task.WhenAll(tasks);
        }

        // Slow path: materialize to list
        return ExecuteBatchSlowPathAsync(items, operation);
    }

    private static async Task ExecuteBatchSlowPathAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        var tasks = new Task[itemList.Count];
        for (int i = 0; i < itemList.Count; i++)
            tasks[i] = operation(itemList[i]);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute batch async operations with parameter in parallel
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        if (items is ICollection<T> collection)
        {
            if (collection.Count == 0)
                return Task.CompletedTask;

            var tasks = new Task[collection.Count];
            int index = 0;
            foreach (var item in collection)
                tasks[index++] = operation(item, parameter);

            return Task.WhenAll(tasks);
        }

        return ExecuteBatchSlowPathAsync(items, parameter, operation);
    }

    private static async Task ExecuteBatchSlowPathAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        var tasks = new Task[itemList.Count];
        for (int i = 0; i < itemList.Count; i++)
            tasks[i] = operation(itemList[i], parameter);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
