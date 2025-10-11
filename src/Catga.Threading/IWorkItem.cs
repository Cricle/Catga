namespace Catga.Threading;

/// <summary>
/// Represents a unit of work that can be executed by the thread pool
/// </summary>
public interface IWorkItem
{
    /// <summary>
    /// Gets the priority of this work item (higher = more important)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Executes the work item
    /// </summary>
    void Execute();
}

/// <summary>
/// A simple work item wrapping an Action
/// </summary>
public sealed class ActionWorkItem : IWorkItem
{
    private readonly Action _action;

    public int Priority { get; }

    public ActionWorkItem(Action action, int priority = 0)
    {
        _action = action;
        Priority = priority;
    }

    public void Execute() => _action();
}

/// <summary>
/// A work item wrapping an async operation
/// </summary>
public sealed class AsyncWorkItem : IWorkItem
{
    private readonly Func<Task> _asyncAction;

    public int Priority { get; }

    public AsyncWorkItem(Func<Task> asyncAction, int priority = 0)
    {
        _asyncAction = asyncAction;
        Priority = priority;
    }

    public void Execute()
    {
        // Fire-and-forget async execution
        _ = _asyncAction();
    }
}

