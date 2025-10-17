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
    /// Uses real Duration field from PerformanceMetric events
    /// </summary>
    public async ValueTask<List<SlowQuery>> DetectSlowQueriesAsync(TimeSpan threshold, int topN = 10)
    {
        _logger.LogInformation("Detecting slow queries with threshold {Threshold}ms", threshold.TotalMilliseconds);

        var slowQueries = new List<SlowQuery>();

        // Get recent events
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-1); // Last hour
        var events = await _eventStore.GetEventsAsync(startTime, endTime);

        // Filter for PerformanceMetric events with Duration data
        var perfEvents = events
            .Where(e => e.Type == EventType.PerformanceMetric && e.Duration > 0)
            .Where(e => e.Duration >= threshold.TotalMilliseconds)
            .OrderByDescending(e => e.Duration)
            .Take(topN)
            .ToList();

        foreach (var evt in perfEvents)
        {
            slowQueries.Add(new SlowQuery
            {
                CorrelationId = evt.CorrelationId,
                RequestType = evt.MessageType ?? "Unknown",
                Duration = TimeSpan.FromMilliseconds(evt.Duration),
                Threshold = threshold,
                DetectedAt = DateTime.UtcNow,
                MemoryAllocated = evt.MemoryAllocated,
                CpuTime = evt.CpuTime.HasValue ? TimeSpan.FromMilliseconds(evt.CpuTime.Value) : null,
                ThreadId = evt.ThreadId,
                Timestamp = evt.Timestamp
            });
        }

        _logger.LogInformation("Detected {Count} slow queries out of {Total} performance events", 
            slowQueries.Count, 
            events.Count(e => e.Type == EventType.PerformanceMetric));
        
        return slowQueries;
    }

    /// <summary>
    /// Identifies hot spots (methods consuming most time)
    /// Uses real Duration data from PerformanceMetric events
    /// </summary>
    public async ValueTask<List<HotSpot>> IdentifyHotSpotsAsync(int topN = 10)
    {
        _logger.LogInformation("Identifying top {TopN} hot spots", topN);

        var hotSpots = new List<HotSpot>();

        // Get recent events
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-1);
        var events = await _eventStore.GetEventsAsync(startTime, endTime);

        // Filter for PerformanceMetric events
        var perfEvents = events
            .Where(e => e.Type == EventType.PerformanceMetric && e.Duration > 0)
            .ToList();

        if (!perfEvents.Any())
        {
            _logger.LogInformation("No performance events found for hot spot analysis");
            return hotSpots;
        }

        // Calculate total time for percentage calculation
        var totalTime = perfEvents.Sum(e => e.Duration);

        // Group by message type and calculate statistics
        var messageTypeStats = perfEvents
            .GroupBy(e => e.MessageType ?? "Unknown")
            .Select(g => new
            {
                MessageType = g.Key,
                Count = g.Count(),
                TotalTime = g.Sum(e => e.Duration),
                AvgTime = g.Average(e => e.Duration),
                MinTime = g.Min(e => e.Duration),
                MaxTime = g.Max(e => e.Duration),
                TotalMemory = g.Sum(e => e.MemoryAllocated ?? 0),
                TotalCpuTime = g.Sum(e => e.CpuTime ?? 0)
            })
            .OrderByDescending(s => s.TotalTime)
            .Take(topN)
            .ToList();

        foreach (var stat in messageTypeStats)
        {
            hotSpots.Add(new HotSpot
            {
                MethodName = stat.MessageType,
                TotalTime = TimeSpan.FromMilliseconds(stat.TotalTime),
                CallCount = stat.Count,
                AverageTime = TimeSpan.FromMilliseconds(stat.AvgTime),
                MinTime = TimeSpan.FromMilliseconds(stat.MinTime),
                MaxTime = TimeSpan.FromMilliseconds(stat.MaxTime),
                PercentageOfTotal = totalTime > 0 ? (stat.TotalTime / totalTime) * 100 : 0,
                TotalMemoryAllocated = stat.TotalMemory,
                TotalCpuTime = TimeSpan.FromMilliseconds(stat.TotalCpuTime)
            });
        }

        _logger.LogInformation("Identified {Count} hot spots from {Total} performance events", 
            hotSpots.Count, 
            perfEvents.Count);
        
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
/// Represents a slow query with detailed performance metrics
/// </summary>
public sealed class SlowQuery
{
    public string CorrelationId { get; set; } = "";
    public string RequestType { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public TimeSpan Threshold { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime Timestamp { get; set; }
    public long? MemoryAllocated { get; set; }
    public TimeSpan? CpuTime { get; set; }
    public int? ThreadId { get; set; }

    public double SlownessFactor => Duration.TotalMilliseconds / Threshold.TotalMilliseconds;
}

/// <summary>
/// Represents a performance hot spot with comprehensive statistics
/// </summary>
public sealed class HotSpot
{
    public string MethodName { get; set; } = "";
    public TimeSpan TotalTime { get; set; }
    public int CallCount { get; set; }
    public TimeSpan AverageTime { get; set; }
    public TimeSpan MinTime { get; set; }
    public TimeSpan MaxTime { get; set; }
    public double PercentageOfTotal { get; set; }
    public long TotalMemoryAllocated { get; set; }
    public TimeSpan TotalCpuTime { get; set; }
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

