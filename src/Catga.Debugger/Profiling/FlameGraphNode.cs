using System.Collections.Generic;

namespace Catga.Debugger.Profiling;

/// <summary>
/// Represents a node in a flame graph
/// </summary>
public sealed class FlameGraphNode
{
    /// <summary>
    /// Name of this node (method/function name)
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Full qualified name
    /// </summary>
    public string FullName { get; set; }

    /// <summary>
    /// Time spent in this node only (excluding children)
    /// </summary>
    public long SelfTime { get; set; }

    /// <summary>
    /// Total time spent in this node (including children)
    /// </summary>
    public long TotalTime { get; set; }

    /// <summary>
    /// Number of samples
    /// </summary>
    public long SampleCount { get; set; }

    /// <summary>
    /// Child nodes
    /// </summary>
    public List<FlameGraphNode> Children { get; set; } = new();

    /// <summary>
    /// Parent node (null for root)
    /// </summary>
    public FlameGraphNode? Parent { get; set; }

    /// <summary>
    /// Depth in the tree (0 = root)
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Percentage of total time
    /// </summary>
    public double Percentage { get; set; }

    public FlameGraphNode(string name, string fullName)
    {
        Name = name;
        FullName = fullName;
    }

    /// <summary>
    /// Adds a child node
    /// </summary>
    public FlameGraphNode AddChild(string name, string fullName)
    {
        var child = new FlameGraphNode(name, fullName)
        {
            Parent = this,
            Depth = Depth + 1
        };
        Children.Add(child);
        return child;
    }

    /// <summary>
    /// Calculates percentages for this node and all children
    /// </summary>
    public void CalculatePercentages(long totalSamples)
    {
        if (totalSamples > 0)
        {
            Percentage = (double)TotalTime / totalSamples * 100.0;
        }

        foreach (var child in Children)
        {
            child.CalculatePercentages(totalSamples);
        }
    }
}

