namespace Catga.Configuration;

/// <summary>
/// Thread pool configuration options
/// </summary>
public class ThreadPoolOptions
{
    /// <summary>
    /// Minimum worker threads for the thread pool
    /// </summary>
    public int MinWorkerThreads { get; set; } = 0;

    /// <summary>
    /// Minimum I/O threads for the thread pool
    /// </summary>
    public int MinIOThreads { get; set; } = 0;

    /// <summary>
    /// Maximum concurrent event handlers
    /// </summary>
    public int MaxEventHandlerConcurrency { get; set; } = 0; // 0 = unlimited

    /// <summary>
    /// Use dedicated long-running thread for background tasks
    /// </summary>
    public bool UseLongRunningThreads { get; set; } = false;
}

