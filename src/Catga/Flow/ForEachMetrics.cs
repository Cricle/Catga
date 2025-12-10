using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Flow.Dsl;

/// <summary>
/// Performance metrics for ForEach operations.
/// </summary>
public class ForEachMetrics
{
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan AverageItemDuration { get; set; }
    public double ItemsPerSecond { get; set; }
    public int MaxConcurrency { get; set; }
    public int BatchCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// Metrics collector for ForEach operations.
/// </summary>
internal class ForEachMetricsCollector : IDisposable
{
    private static readonly Meter Meter = new("Catga.Flow.ForEach", "1.0.0");

    private static readonly Counter<int> ItemsProcessedCounter =
        Meter.CreateCounter<int>("foreach_items_processed_total", "items", "Total number of items processed");

    private static readonly Counter<int> ItemsFailedCounter =
        Meter.CreateCounter<int>("foreach_items_failed_total", "items", "Total number of items that failed processing");

    private static readonly Histogram<double> ItemDurationHistogram =
        Meter.CreateHistogram<double>("foreach_item_duration_seconds", "seconds", "Duration of individual item processing");

    private static readonly Histogram<double> BatchDurationHistogram =
        Meter.CreateHistogram<double>("foreach_batch_duration_seconds", "seconds", "Duration of batch processing");

    private static readonly Gauge<int> ActiveConcurrencyGauge =
        Meter.CreateGauge<int>("foreach_active_concurrency", "items", "Current number of items being processed concurrently");

    private readonly Stopwatch _totalStopwatch;
    private readonly string _flowId;
    private readonly int _stepIndex;
    private readonly ForEachMetrics _metrics;
    private int _activeConcurrency;
    private readonly object _lock = new();

    public ForEachMetricsCollector(string flowId, int stepIndex)
    {
        _flowId = flowId;
        _stepIndex = stepIndex;
        _totalStopwatch = Stopwatch.StartNew();
        _metrics = new ForEachMetrics
        {
            StartTime = DateTime.UtcNow
        };
    }

    public void SetTotalItems(int totalItems)
    {
        _metrics.TotalItems = totalItems;
    }

    public void RecordItemStarted()
    {
        lock (_lock)
        {
            _activeConcurrency++;
            _metrics.MaxConcurrency = Math.Max(_metrics.MaxConcurrency, _activeConcurrency);
            ActiveConcurrencyGauge.Record(_activeConcurrency,
                new KeyValuePair<string, object?>("flow_id", _flowId),
                new KeyValuePair<string, object?>("step_index", _stepIndex));
        }
    }

    public void RecordItemCompleted(TimeSpan duration, bool success)
    {
        lock (_lock)
        {
            _activeConcurrency--;

            if (success)
            {
                _metrics.ProcessedItems++;
                ItemsProcessedCounter.Add(1,
                    new KeyValuePair<string, object?>("flow_id", _flowId),
                    new KeyValuePair<string, object?>("step_index", _stepIndex));
            }
            else
            {
                _metrics.FailedItems++;
                ItemsFailedCounter.Add(1,
                    new KeyValuePair<string, object?>("flow_id", _flowId),
                    new KeyValuePair<string, object?>("step_index", _stepIndex));
            }

            ItemDurationHistogram.Record(duration.TotalSeconds,
                new KeyValuePair<string, object?>("flow_id", _flowId),
                new KeyValuePair<string, object?>("step_index", _stepIndex),
                new KeyValuePair<string, object?>("success", success));

            ActiveConcurrencyGauge.Record(_activeConcurrency,
                new KeyValuePair<string, object?>("flow_id", _flowId),
                new KeyValuePair<string, object?>("step_index", _stepIndex));
        }
    }

    public void RecordBatchCompleted(TimeSpan duration, int itemCount)
    {
        _metrics.BatchCount++;
        BatchDurationHistogram.Record(duration.TotalSeconds,
            new KeyValuePair<string, object?>("flow_id", _flowId),
            new KeyValuePair<string, object?>("step_index", _stepIndex),
            new KeyValuePair<string, object?>("batch_size", itemCount));
    }

    public ForEachMetrics Complete()
    {
        _totalStopwatch.Stop();
        _metrics.EndTime = DateTime.UtcNow;
        _metrics.TotalDuration = _totalStopwatch.Elapsed;

        if (_metrics.ProcessedItems > 0)
        {
            _metrics.AverageItemDuration = TimeSpan.FromTicks(_metrics.TotalDuration.Ticks / _metrics.ProcessedItems);
            _metrics.ItemsPerSecond = _metrics.ProcessedItems / _metrics.TotalDuration.TotalSeconds;
        }

        return _metrics;
    }

    public void Dispose()
    {
        _totalStopwatch?.Stop();
    }
}

/// <summary>
/// Item processing metrics tracker.
/// </summary>
internal class ItemMetricsTracker : IDisposable
{
    private readonly ForEachMetricsCollector _collector;
    private readonly Stopwatch _stopwatch;

    public ItemMetricsTracker(ForEachMetricsCollector collector)
    {
        _collector = collector;
        _stopwatch = Stopwatch.StartNew();
        _collector.RecordItemStarted();
    }

    public void Complete(bool success)
    {
        _stopwatch.Stop();
        _collector.RecordItemCompleted(_stopwatch.Elapsed, success);
    }

    public void Dispose()
    {
        if (_stopwatch.IsRunning)
        {
            Complete(false); // Assume failure if not explicitly completed
        }
    }
}
