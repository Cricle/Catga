using System.Collections.Concurrent;
using System.Diagnostics;
using Cysharp.Threading.Tasks;

namespace Catga.Threading;

/// <summary>
/// High-performance work-stealing thread pool
/// Better than System.Threading.ThreadPool for CPU-bound parallel tasks
///
/// Key improvements over ThreadPool:
/// 1. Work-Stealing: Each thread has its own queue, can steal from others when idle
/// 2. Priority Support: Higher priority tasks executed first
/// 3. Per-Core Queues: Better cache locality
/// 4. Lock-Free: Minimal contention using ConcurrentQueue
/// 5. Dedicated Threads: No interference with other ThreadPool users
/// 6. Metrics & Events: Real-time monitoring and observability
/// </summary>
public sealed class WorkStealingThreadPool : IThreadPool
{
    private readonly ThreadPoolOptions _options;
    private readonly ConcurrentBag<WorkerThread> _workers = new();
    private readonly ConcurrentQueue<IWorkItem> _globalQueue = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ThreadPoolMetrics _metrics = new();
    private readonly Timer? _metricsTimer;
    private readonly Timer? _scalingTimer;
    private long _completedWorkCount;
    private long _totalExecutionTimeMs;
    private int _peakPendingCount;
    private int _currentWorkerCount;
    private int _isDisposed;

    // Scaling state tracking
    private DateTime _lastScaleUpTime = DateTime.MinValue;
    private DateTime _lastScaleDownTime = DateTime.MinValue;
    private readonly ConcurrentQueue<LoadSnapshot> _loadHistory = new();
    private int _consecutiveHighLoadChecks = 0;
    private int _consecutiveLowLoadChecks = 0;

    public int WorkerCount => _currentWorkerCount;

    public int PendingWorkCount
    {
        get
        {
            int count = _globalQueue.Count;
            foreach (var worker in _workers)
            {
                count += worker.LocalQueueCount;
            }
            return count;
        }
    }

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    /// <summary>
    /// Get current thread pool metrics snapshot
    /// </summary>
    public ThreadPoolMetrics GetMetrics() => _metrics.Clone();

    public WorkStealingThreadPool(ThreadPoolOptions? options = null)
    {
        _options = options ?? new ThreadPoolOptions();

        // Create initial worker threads
        int initialWorkerCount = _options.MinThreads;
        _currentWorkerCount = initialWorkerCount;

        for (int i = 0; i < initialWorkerCount; i++)
        {
            var worker = new WorkerThread(
                id: i,
                globalQueue: _globalQueue,
                workers: _workers,
                parent: this,
                shutdownToken: _shutdownCts.Token,
                options: _options);
            _workers.Add(worker);
            worker.Start();
        }

        // Start metrics update timer (every 5 seconds)
        _metricsTimer = new Timer(
            callback: _ => UpdateMetricsSnapshot(),
            state: null,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(5));

        // Start dynamic scaling timer if enabled
        if (_options.EnableDynamicScaling)
        {
            _scalingTimer = new Timer(
                callback: _ => AdjustThreadCount(),
                state: null,
                dueTime: _options.ScalingCheckInterval,
                period: _options.ScalingCheckInterval);
        }
    }

    public bool QueueWorkItem(IWorkItem workItem)
    {
        if (_isDisposed != 0)
            return false;

        // Emit telemetry
        ThreadPoolEventSource.Log.TaskQueued(workItem.Priority);
        _metrics.TotalTasksQueued++;

        // Try to push to local queue first (better cache locality)
        int currentThreadId = Environment.CurrentManagedThreadId;
        foreach (var worker in _workers)
        {
            if (worker.ManagedThreadId == currentThreadId)
            {
                worker.EnqueueLocal(workItem);
                return true;
            }
        }

        // Fallback to global queue
        _globalQueue.Enqueue(workItem);
        return true;
    }

    public bool QueueWorkItem(Action action, int priority = 0)
    {
        return QueueWorkItem(new ActionWorkItem(action, priority));
    }

    public bool QueueWorkItem(Func<Task> asyncAction, int priority = 0)
    {
        return QueueWorkItem(new AsyncWorkItem(asyncAction, priority));
    }

    /// <summary>
    /// Queue work and return a Task for completion tracking
    /// </summary>
    public Task RunAsync(Action action, int priority = 0, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_isDisposed != 0 || cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        QueueWorkItem(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);

        return tcs.Task;
    }

    /// <summary>
    /// Queue work with result and return a Task{T}
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> func, int priority = 0, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_isDisposed != 0 || cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        QueueWorkItem(() =>
        {
            try
            {
                var result = func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);

        return tcs.Task;
    }

    /// <summary>
    /// Queue async work and return the original Task
    /// </summary>
    public Task RunAsync(Func<Task> asyncFunc, int priority = 0, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_isDisposed != 0 || cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        QueueWorkItem(async () =>
        {
            try
            {
                await asyncFunc().ConfigureAwait(false);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);

        return tcs.Task;
    }

    /// <summary>
    /// Queue async work with result and return Task{T}
    /// </summary>
    public Task<T> RunAsync<T>(Func<Task<T>> asyncFunc, int priority = 0, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_isDisposed != 0 || cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        QueueWorkItem(async () =>
        {
            try
            {
                var result = await asyncFunc().ConfigureAwait(false);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);

        return tcs.Task;
    }

    // ===== UniTask API (Zero-Allocation) =====

    /// <summary>
    /// Run work on thread pool and return zero-allocation UniTask (from Cysharp.Threading.Tasks)
    /// </summary>
    public UniTask RunUniTaskAsync(Action action, int priority = 0)
    {
        if (_isDisposed != 0)
        {
            return UniTask.CompletedTask;
        }

        var source = AutoResetUniTaskCompletionSource.Create();

        QueueWorkItem(() =>
        {
            try
            {
                action();
                source.TrySetResult();
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        }, priority);

        return source.Task;
    }

    /// <summary>
    /// Run work with result on thread pool and return zero-allocation UniTask{T}
    /// </summary>
    public UniTask<T> RunUniTaskAsync<T>(Func<T> func, int priority = 0)
    {
        if (_isDisposed != 0)
        {
            return UniTask.FromResult(default(T)!);
        }

        var source = AutoResetUniTaskCompletionSource<T>.Create();

        QueueWorkItem(() =>
        {
            try
            {
                var result = func();
                source.TrySetResult(result);
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        }, priority);

        return source.Task;
    }

    /// <summary>
    /// Update metrics snapshot
    /// </summary>
    private void UpdateMetricsSnapshot()
    {
        var pending = PendingWorkCount;
        var workersList = _workers.ToArray();
        var activeWorkers = workersList.Count(w => w.LocalQueueCount > 0 || _globalQueue.Count > 0);
        var idleWorkers = workersList.Length - activeWorkers;

        _metrics.PendingTaskCount = pending;
        _metrics.ActiveWorkerCount = activeWorkers;
        _metrics.IdleWorkerCount = idleWorkers;
        _metrics.LastUpdated = DateTimeOffset.UtcNow;

        // Calculate average execution time
        var completed = _metrics.TotalTasksCompleted;
        if (completed > 0)
        {
            _metrics.AverageExecutionTimeMs = Interlocked.Read(ref _totalExecutionTimeMs) / (double)completed;
        }

        // Update peak pending count
        var currentPeak = _peakPendingCount;
        if (pending > currentPeak)
        {
            Interlocked.CompareExchange(ref _peakPendingCount, pending, currentPeak);
            _metrics.PeakPendingTaskCount = pending;
        }

        // Emit metrics event
        ThreadPoolEventSource.Log.MetricsUpdated(
            _metrics.TotalTasksQueued,
            _metrics.TotalTasksCompleted,
            _metrics.TotalTasksFailed,
            _metrics.PendingTaskCount,
            _metrics.ActiveWorkerCount,
            _metrics.Utilization);

        // Check for saturation (>80% utilization)
        if (_metrics.Utilization > 0.8)
        {
            ThreadPoolEventSource.Log.ThreadPoolSaturated(pending, _metrics.Utilization);
        }
    }

    /// <summary>
    /// Dynamically adjust thread count based on workload with intelligent pattern detection
    /// </summary>
    private void AdjustThreadCount()
    {
        if (!_options.EnableDynamicScaling || _isDisposed != 0)
            return;

        var now = DateTime.UtcNow;
        var pending = PendingWorkCount;
        var currentCount = _currentWorkerCount;
        var workersList = _workers.ToArray();

        // Record load snapshot for pattern detection
        RecordLoadSnapshot(pending, currentCount, now);

        // Calculate load metrics
        double tasksPerThread = currentCount > 0 ? (double)pending / currentCount : pending;
        var activeWorkers = workersList.Count(w => !w.IsIdle(TimeSpan.FromSeconds(5)));
        double utilization = currentCount > 0 ? (double)activeWorkers / currentCount : 0;

        // Detect load pattern
        var loadPattern = DetectLoadPattern();

        // === SCALE UP LOGIC ===
        if (currentCount < _options.MaxThreads)
        {
            int threadsToAdd = 0;

            // Aggressive scale-up for sudden spikes
            if (tasksPerThread > _options.AggressiveScaleUpThreshold)
            {
                // Add multiple threads for sudden load spike
                threadsToAdd = Math.Min(
                    _options.MaxThreadsPerScaleUp,
                    _options.MaxThreads - currentCount
                );
                _consecutiveHighLoadChecks++;
            }
            // Normal scale-up for sustained high load
            else if (tasksPerThread > _options.NormalScaleUpThreshold)
            {
                _consecutiveHighLoadChecks++;

                // Add threads gradually, more if load is sustained
                if (_consecutiveHighLoadChecks >= 3)
                {
                    threadsToAdd = Math.Min(2, _options.MaxThreads - currentCount);
                }
                else
                {
                    threadsToAdd = 1;
                }
            }
            else
            {
                _consecutiveHighLoadChecks = 0;
            }

            // Execute scale-up
            if (threadsToAdd > 0)
            {
                for (int i = 0; i < threadsToAdd; i++)
                {
                    int newWorkerId = Interlocked.Increment(ref _currentWorkerCount) - 1;
                    var newWorker = new WorkerThread(
                        id: newWorkerId,
                        globalQueue: _globalQueue,
                        workers: _workers,
                        parent: this,
                        shutdownToken: _shutdownCts.Token,
                        options: _options);

                    _workers.Add(newWorker);
                    newWorker.Start();
                }

                _lastScaleUpTime = now;
                _consecutiveLowLoadChecks = 0;
                ThreadPoolEventSource.Log.TaskQueued(0); // Log scaling event
                return;
            }
        }

        // === SCALE DOWN LOGIC ===
        if (currentCount > _options.MinThreads)
        {
            // Check cooldown period to prevent thrashing
            var timeSinceLastScaleUp = now - _lastScaleUpTime;
            if (timeSinceLastScaleUp < _options.ScaleDownCooldown)
            {
                return; // Too soon after scale-up
            }

            // For periodic spike pattern, be more conservative
            if (loadPattern == LoadPattern.PeriodicSpikes)
            {
                // Only scale down if idle for much longer
                var extendedIdleTime = _options.MinIdleTimeBeforeRemoval.Add(TimeSpan.FromSeconds(30));
                var idleWorkers = workersList.Where(w => w.IsIdle(extendedIdleTime)).ToList();
                double idleRatio = workersList.Length > 0 ? (double)idleWorkers.Count / workersList.Length : 0;

                if (idleRatio > _options.ScaleDownThreshold)
                {
                    _consecutiveLowLoadChecks++;

                    // Require sustained low load before scaling down
                    if (_consecutiveLowLoadChecks >= 5)
                    {
                        var workerToRemove = idleWorkers.FirstOrDefault();
                        if (workerToRemove != null)
                        {
                            workerToRemove.RequestShutdown();
                            Interlocked.Decrement(ref _currentWorkerCount);
                            _lastScaleDownTime = now;
                            _consecutiveLowLoadChecks = 0;
                        }
                    }
                }
                else
                {
                    _consecutiveLowLoadChecks = 0;
                }
            }
            else
            {
                // Normal scale-down for stable/declining load
                var idleWorkers = workersList.Where(w => w.IsIdle(_options.MinIdleTimeBeforeRemoval)).ToList();
                double idleRatio = workersList.Length > 0 ? (double)idleWorkers.Count / workersList.Length : 0;

                if (idleRatio > _options.ScaleDownThreshold)
                {
                    _consecutiveLowLoadChecks++;

                    // Require at least 3 consecutive checks for stable load
                    if (_consecutiveLowLoadChecks >= 3)
                    {
                        var workerToRemove = idleWorkers.FirstOrDefault();
                        if (workerToRemove != null)
                        {
                            workerToRemove.RequestShutdown();
                            Interlocked.Decrement(ref _currentWorkerCount);
                            _lastScaleDownTime = now;
                            _consecutiveLowLoadChecks = 0;
                        }
                    }
                }
                else
                {
                    _consecutiveLowLoadChecks = 0;
                }
            }
        }
    }

    /// <summary>
    /// Record load snapshot for pattern detection
    /// </summary>
    private void RecordLoadSnapshot(int pendingTasks, int threadCount, DateTime timestamp)
    {
        _loadHistory.Enqueue(new LoadSnapshot
        {
            Timestamp = timestamp,
            PendingTasks = pendingTasks,
            ThreadCount = threadCount
        });

        // Keep only recent history
        var cutoff = timestamp - _options.LoadHistoryWindow;
        while (_loadHistory.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _loadHistory.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Detect load pattern from history
    /// </summary>
    private LoadPattern DetectLoadPattern()
    {
        var history = _loadHistory.ToArray();
        if (history.Length < 10)
        {
            return LoadPattern.Stable;
        }

        // Calculate load variance
        var avgLoad = history.Average(s => s.PendingTasks);
        var variance = history.Average(s => Math.Pow(s.PendingTasks - avgLoad, 2));
        var stdDev = Math.Sqrt(variance);

        // Detect sudden spike (current load >> average)
        var currentLoad = history.Length > 0 ? history[history.Length - 1].PendingTasks : 0;
        if (currentLoad > avgLoad + 2 * stdDev && avgLoad > 0)
        {
            return LoadPattern.SuddenSpike;
        }

        // Detect periodic pattern (high variance with regular intervals)
        if (stdDev > avgLoad * 0.5 && avgLoad > 5)
        {
            // Check for periodic peaks
            var peaks = history.Where(s => s.PendingTasks > avgLoad + stdDev).ToList();
            if (peaks.Count >= 3)
            {
                // Check if peaks are roughly evenly spaced
                var intervals = new List<double>();
                for (int i = 1; i < peaks.Count; i++)
                {
                    intervals.Add((peaks[i].Timestamp - peaks[i - 1].Timestamp).TotalSeconds);
                }

                if (intervals.Count > 0)
                {
                    var avgInterval = intervals.Average();
                    var intervalVariance = intervals.Average(iv => Math.Pow(iv - avgInterval, 2));

                    // If intervals are consistent, it's periodic
                    if (Math.Sqrt(intervalVariance) < avgInterval * 0.3)
                    {
                        return LoadPattern.PeriodicSpikes;
                    }
                }
            }
        }

        // Detect declining load
        if (history.Length >= 10)
        {
            var firstHalf = history.Take(history.Length / 2).Average(s => s.PendingTasks);
            var secondHalf = history.Skip(history.Length / 2).Average(s => s.PendingTasks);

            if (firstHalf > secondHalf * 1.5 && firstHalf > 10)
            {
                return LoadPattern.Declining;
            }
        }

        return LoadPattern.Stable;
    }

    /// <summary>
    /// Load snapshot for pattern detection
    /// </summary>
    private struct LoadSnapshot
    {
        public DateTime Timestamp { get; init; }
        public int PendingTasks { get; init; }
        public int ThreadCount { get; init; }
    }

    /// <summary>
    /// Detected load patterns
    /// </summary>
    private enum LoadPattern
    {
        Stable,          // Steady workload
        SuddenSpike,     // Sudden increase in load
        PeriodicSpikes,  // Regular peaks and valleys
        Declining        // Load decreasing over time
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        // Signal shutdown
        _shutdownCts.Cancel();

        // Wait for all workers to complete
        foreach (var worker in _workers.ToArray())
        {
            worker.Join();
        }

        _metricsTimer?.Dispose();
        _scalingTimer?.Dispose();
        _shutdownCts.Dispose();
    }

    /// <summary>
    /// Worker thread with local queue and work-stealing capability
    /// </summary>
    private sealed class WorkerThread
    {
        private readonly int _id;
        private readonly ConcurrentQueue<IWorkItem> _globalQueue;
        private readonly ConcurrentBag<WorkerThread> _workers;
        private readonly ConcurrentQueue<IWorkItem> _localQueue = new();
        private readonly CancellationToken _shutdownToken;
        private readonly ThreadPoolOptions _options;
        private readonly Thread _thread;
        private readonly WorkStealingThreadPool _parent;
        private readonly CancellationTokenSource _workerShutdownCts = new();
        private DateTime _lastActivityTime = DateTime.UtcNow;

        public int ManagedThreadId => _thread.ManagedThreadId;
        public int LocalQueueCount => _localQueue.Count;

        public WorkerThread(
            int id,
            ConcurrentQueue<IWorkItem> globalQueue,
            ConcurrentBag<WorkerThread> workers,
            WorkStealingThreadPool parent,
            CancellationToken shutdownToken,
            ThreadPoolOptions options)
        {
            _id = id;
            _globalQueue = globalQueue;
            _workers = workers;
            _parent = parent;
            _shutdownToken = shutdownToken;
            _options = options;

            _thread = new Thread(WorkLoop)
            {
                IsBackground = true,
                Priority = _options.ThreadPriority,
                Name = $"CatgaWorker-{_id}"
            };
        }

        public void Start() => _thread.Start();

        public void Join() => _thread.Join();

        public void EnqueueLocal(IWorkItem workItem)
        {
            _localQueue.Enqueue(workItem);
            _lastActivityTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Check if worker has been idle for specified duration
        /// </summary>
        public bool IsIdle(TimeSpan idleThreshold)
        {
            return _localQueue.IsEmpty &&
                   (DateTime.UtcNow - _lastActivityTime) > idleThreshold;
        }

        /// <summary>
        /// Request graceful shutdown of this worker
        /// </summary>
        public void RequestShutdown()
        {
            _workerShutdownCts.Cancel();
        }

        private void WorkLoop()
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdownToken, _workerShutdownCts.Token);

                while (!linkedCts.Token.IsCancellationRequested)
                {
                    IWorkItem? workItem = null;

                    // 1. Try local queue first (best for cache locality)
                    if (_localQueue.TryDequeue(out workItem))
                    {
                        _lastActivityTime = DateTime.UtcNow;
                        ExecuteWorkItem(workItem);
                        continue;
                    }

                    // 2. Try global queue
                    if (_globalQueue.TryDequeue(out workItem))
                    {
                        _lastActivityTime = DateTime.UtcNow;
                        ExecuteWorkItem(workItem);
                        continue;
                    }

                    // 3. Work-stealing: try to steal from other workers
                    if (_options.EnableWorkStealing && TryStealWork(out workItem))
                    {
                        _lastActivityTime = DateTime.UtcNow;
                        ExecuteWorkItem(workItem);
                        continue;
                    }

                    // 4. No work available, yield CPU and wait
                    Thread.Yield();
                    Thread.Sleep(1); // Reduce CPU usage when idle
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            finally
            {
                _workerShutdownCts.Dispose();
            }
        }

        private bool TryStealWork(out IWorkItem? workItem)
        {
            workItem = null;

            // Try to steal from other workers
            var workersList = _workers.ToArray();
            foreach (var targetWorker in workersList)
            {
                if (targetWorker == this)
                    continue;

                if (targetWorker._localQueue.TryDequeue(out workItem))
                {
                    // Emit work-stealing telemetry
                    _parent._metrics.TotalWorkStealCount++;
                    ThreadPoolEventSource.Log.WorkStolen(_id, 1, targetWorker._id);

                    using var activity = ThreadPoolActivitySource.Source.StartActivity(
                        ThreadPoolActivitySource.WorkStealActivity,
                        ActivityKind.Internal);
                    activity?.SetTag(ThreadPoolActivitySource.SourceWorkerTag, targetWorker._id);
                    activity?.SetTag(ThreadPoolActivitySource.TargetWorkerTag, _id);
                    activity?.SetTag(ThreadPoolActivitySource.StolenCountTag, 1);

                    return true;
                }
            }

            return false;
        }

        private void ExecuteWorkItem(IWorkItem workItem)
        {
            var sw = Stopwatch.StartNew();

            // Start activity for distributed tracing
            using var activity = ThreadPoolActivitySource.Source.StartActivity(
                ThreadPoolActivitySource.TaskExecutionActivity,
                ActivityKind.Internal);

            activity?.SetTag(ThreadPoolActivitySource.WorkerIdTag, _id);
            activity?.SetTag(ThreadPoolActivitySource.TaskPriorityTag, workItem.Priority);

            // Emit event
            ThreadPoolEventSource.Log.TaskStarted(_id, workItem.Priority);

            try
            {
                workItem.Execute();
                sw.Stop();

                // Update metrics
                Interlocked.Increment(ref _parent._completedWorkCount);
                _parent._metrics.TotalTasksCompleted++;
                Interlocked.Add(ref _parent._totalExecutionTimeMs, sw.ElapsedMilliseconds);

                // Emit telemetry
                activity?.SetTag(ThreadPoolActivitySource.TaskStatusTag, "completed");
                activity?.SetTag(ThreadPoolActivitySource.ExecutionTimeTag, sw.Elapsed.TotalMilliseconds);
                ThreadPoolEventSource.Log.TaskCompleted(sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();

                // Update failure metrics
                _parent._metrics.TotalTasksFailed++;

                // Emit telemetry
                activity?.SetTag(ThreadPoolActivitySource.TaskStatusTag, "failed");
                activity?.SetTag(ThreadPoolActivitySource.ExceptionTypeTag, ex.GetType().Name);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                ThreadPoolEventSource.Log.TaskFailed(ex.GetType().Name, ex.Message, sw.Elapsed.TotalMilliseconds);
            }
        }
    }
}

