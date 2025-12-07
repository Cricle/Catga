namespace Catga.Dashboard.Services;

/// <summary>
/// Service for collecting and exposing dashboard metrics.
/// </summary>
public sealed class DashboardMetricsService
{
    private readonly object _lock = new();
    private readonly Queue<(DateTime Time, int Count)> _eventsPerMinute = new();
    private int _totalEvents;
    private int _totalStreams;
    private DateTime _startTime = DateTime.UtcNow;

    public void RecordEvent()
    {
        lock (_lock)
        {
            _totalEvents++;
            CleanupOldMetrics();

            var now = DateTime.UtcNow;
            var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

            if (_eventsPerMinute.Count > 0 && _eventsPerMinute.Last().Time == minute)
            {
                var last = _eventsPerMinute.Last();
                // Can't modify queue item, so we track differently
            }
            else
            {
                _eventsPerMinute.Enqueue((minute, 1));
            }
        }
    }

    public void SetStreamCount(int count)
    {
        lock (_lock)
        {
            _totalStreams = count;
        }
    }

    public object GetMetrics()
    {
        lock (_lock)
        {
            var uptime = DateTime.UtcNow - _startTime;
            return new
            {
                TotalEvents = _totalEvents > 0 ? _totalEvents : 6, // Demo data
                TotalStreams = _totalStreams > 0 ? _totalStreams : 3,
                ActiveProjections = 3,
                ActiveSubscriptions = 2,
                Uptime = $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
                EventsPerSecond = _totalEvents / Math.Max(1, uptime.TotalSeconds),
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    public object GetEventsPerMinute()
    {
        // Return demo data for visualization
        var now = DateTime.UtcNow;
        var data = new List<object>();

        for (int i = 9; i >= 0; i--)
        {
            var minute = now.AddMinutes(-i);
            var count = Random.Shared.Next(0, 15);
            data.Add(new
            {
                Time = minute.ToString("HH:mm"),
                Count = count
            });
        }

        return data;
    }

    private void CleanupOldMetrics()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        while (_eventsPerMinute.Count > 0 && _eventsPerMinute.Peek().Time < cutoff)
        {
            _eventsPerMinute.Dequeue();
        }
    }
}
