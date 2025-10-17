using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Catga.Debugger.Models;
using Catga.Debugger.Storage;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Profiling;

/// <summary>
/// Analyzes performance data to detect bottlenecks and slow queries.
/// Production-safe: Only analyzes historical data.
/// </summary>
public sealed class PerformanceAnalyzer
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<PerformanceAnalyzer> _logger;

    public PerformanceAnalyzer(IEventStore eventStore, ILogger<PerformanceAnalyzer> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    /// <summary>
    /// Detects slow queries (requests exceeding threshold)
    /// </summary>
    public async ValueTask<List<SlowQuery>> DetectSlowQueriesAsync(TimeSpan threshold, int topN = 10)
    {
        _logger.LogInformation("Detecting slow queries with threshold {Threshold}ms", threshold.TotalMilliseconds);

        var stats = await _eventStore.GetStatsAsync();
        var slowQueries = new List<SlowQuery>();

        // Get recent events
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-1); // Last hour
        var events = await _eventStore.GetEventsAsync(startTime, endTime);

        // Group by correlation ID and calculate durations
        var flowDurations = events
            .GroupBy(e => e.CorrelationId)
            .Select(g => new
            {
                CorrelationId = g.Key,
                Events = g.OrderBy(e => e.Timestamp).ToList()
            })
            .Where(f => f.Events.Count > 0)
            .Select(f => new
            {
                f.CorrelationId,
                RequestType = f.Events.FirstOrDefault()?.Type.ToString() ?? "Unknown",
                Duration = f.Events.Last().Timestamp - f.Events.First().Timestamp,
                EventCount = f.Events.Count
            })
            .Where(f => f.Duration > threshold)
            .OrderByDescending(f => f.Duration)
            .Take(topN);

        foreach (var flow in flowDurations)
        {
            slowQueries.Add(new SlowQuery
            {
                CorrelationId = flow.CorrelationId,
                RequestType = flow.RequestType,
                Duration = flow.Duration,
                Threshold = threshold,
                DetectedAt = DateTime.UtcNow
            });
        }

        _logger.LogInformation("Detected {Count} slow queries", slowQueries.Count);
        return slowQueries;
    }

    /// <summary>
    /// Identifies hot spots (methods consuming most time)
    /// </summary>
    public async ValueTask<List<HotSpot>> IdentifyHotSpotsAsync(int topN = 10)
    {
        _logger.LogInformation("Identifying top {TopN} hot spots", topN);

        var stats = await _eventStore.GetStatsAsync();
        var hotSpots = new List<HotSpot>();

        // Get recent events
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-1);
        var events = await _eventStore.GetEventsAsync(startTime, endTime);

        // Group by event type and calculate total time
        var eventStats = events
            .GroupBy(e => e.Type.ToString())
            .Select(g => new
            {
                EventType = g.Key,
                Count = g.Count(),
                TotalTime = g.Sum(e => 100) // Simplified: assume 100ms per event
            })
            .OrderByDescending(s => s.TotalTime)
            .Take(topN);

        foreach (var stat in eventStats)
        {
            hotSpots.Add(new HotSpot
            {
                MethodName = stat.EventType,
                TotalTime = TimeSpan.FromMilliseconds(stat.TotalTime),
                CallCount = stat.Count,
                AverageTime = TimeSpan.FromMilliseconds(stat.TotalTime / stat.Count)
            });
        }

        _logger.LogInformation("Identified {Count} hot spots", hotSpots.Count);
        return hotSpots;
    }

    /// <summary>
    /// Analyzes GC pressure (simplified)
    /// </summary>
    public GcAnalysis AnalyzeGcPressure()
    {
        // Note: Real GC analysis would require GC event tracking
        // This is a simplified placeholder
        return new GcAnalysis
        {
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            TotalMemoryBytes = GC.GetTotalMemory(false),
            AnalyzedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Represents a slow query
/// </summary>
public sealed class SlowQuery
{
    public string CorrelationId { get; set; } = "";
    public string RequestType { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public TimeSpan Threshold { get; set; }
    public DateTime DetectedAt { get; set; }

    public double SlownessFactor => Duration.TotalMilliseconds / Threshold.TotalMilliseconds;
}

/// <summary>
/// Represents a performance hot spot
/// </summary>
public sealed class HotSpot
{
    public string MethodName { get; set; } = "";
    public TimeSpan TotalTime { get; set; }
    public int CallCount { get; set; }
    public TimeSpan AverageTime { get; set; }
    public double PercentageOfTotal { get; set; }
}

/// <summary>
/// GC pressure analysis
/// </summary>
public sealed class GcAnalysis
{
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long TotalMemoryBytes { get; set; }
    public DateTime AnalyzedAt { get; set; }

    public bool IsHighPressure => Gen2Collections > 100; // Simplified threshold
}

