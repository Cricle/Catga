namespace Catga.Core;

/// <summary>
/// Helper for batch operations (DRY principle)
/// </summary>
public static class BatchOperationHelper
{
    /// <summary>
    /// Execute batch async operations
    /// </summary>
    public static async Task ExecuteBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        var tasks = new Task[itemList.Count];
        for (int i = 0; i < itemList.Count; i++)
            tasks[i] = operation(itemList[i]);

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Execute batch async operations with parameter
    /// </summary>
    public static async Task ExecuteBatchAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        var tasks = new Task[itemList.Count];
        for (int i = 0; i < itemList.Count; i++)
            tasks[i] = operation(itemList[i], parameter);

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Execute batch async operations with index
    /// </summary>
    public static async Task ExecuteBatchWithIndexAsync<T>(
        IEnumerable<T> items,
        Func<T, int, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateNotNull(items);
        ValidationHelper.ValidateNotNull(operation);

        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        var tasks = new Task[itemList.Count];
        for (int i = 0; i < itemList.Count; i++)
            tasks[i] = operation(itemList[i], i);

        await Task.WhenAll(tasks);
    }
}
