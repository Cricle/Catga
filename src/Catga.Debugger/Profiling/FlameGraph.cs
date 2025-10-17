using System.Collections.Generic;

namespace Catga.Debugger.Profiling;

/// <summary>
/// Represents a flame graph for performance visualization
/// </summary>
public sealed class FlameGraph
{
    /// <summary>
    /// Root node of the flame graph
    /// </summary>
    public FlameGraphNode Root { get; set; }

    /// <summary>
    /// Total number of samples
    /// </summary>
    public long TotalSamples { get; set; }

    /// <summary>
    /// Total time in milliseconds
    /// </summary>
    public double TotalTimeMs { get; set; }

    /// <summary>
    /// Correlation ID (if specific to a flow)
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Type of flame graph (CPU, Memory, etc.)
    /// </summary>
    public FlameGraphType Type { get; set; }

    /// <summary>
    /// Metadata
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();

    public FlameGraph(FlameGraphNode root, FlameGraphType type)
    {
        Root = root;
        Type = type;
    }

    /// <summary>
    /// Calculates all percentages in the graph
    /// </summary>
    public void CalculatePercentages()
    {
        Root.CalculatePercentages(TotalSamples);
    }

    /// <summary>
    /// Gets all hot spots (nodes with > threshold % time)
    /// </summary>
    public List<FlameGraphNode> GetHotSpots(double thresholdPercentage = 5.0)
    {
        var hotSpots = new List<FlameGraphNode>();
        CollectHotSpots(Root, hotSpots, thresholdPercentage);
        return hotSpots;
    }

    private void CollectHotSpots(FlameGraphNode node, List<FlameGraphNode> hotSpots, double threshold)
    {
        if (node.Percentage >= threshold && node.Children.Count == 0)
        {
            hotSpots.Add(node);
        }

        foreach (var child in node.Children)
        {
            CollectHotSpots(child, hotSpots, threshold);
        }
    }
}

/// <summary>
/// Type of flame graph
/// </summary>
public enum FlameGraphType
{
    /// <summary>CPU time</summary>
    Cpu,
    
    /// <summary>Memory allocations</summary>
    Memory,
    
    /// <summary>Wall clock time</summary>
    WallClock,
    
    /// <summary>Async operations</summary>
    Async
}

