using System;
using System.Collections.Generic;
using System.Linq;
using Catga.Debugger.CallStack;
using Catga.Debugger.Storage;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Profiling;

/// <summary>
/// Builds flame graphs from call stack data.
/// Production-safe: Only processes historical data.
/// </summary>
public sealed class FlameGraphBuilder
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<FlameGraphBuilder> _logger;

    public FlameGraphBuilder(IEventStore eventStore, ILogger<FlameGraphBuilder> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    /// <summary>
    /// Builds a CPU flame graph for a specific correlation ID
    /// </summary>
    public async ValueTask<FlameGraph> BuildCpuFlameGraphAsync(string correlationId)
    {
        _logger.LogInformation("Building CPU flame graph for {CorrelationId}", correlationId);

        var events = await _eventStore.GetEventsByCorrelationAsync(correlationId);
        var root = new FlameGraphNode("root", "root");
        var graph = new FlameGraph(root, FlameGraphType.Cpu)
        {
            CorrelationId = correlationId
        };

        // Build call tree from events
        var callStacks = ExtractCallStacks(events);
        long totalSamples = 0;

        foreach (var stack in callStacks)
        {
            totalSamples += stack.Duration;
            AddStackToGraph(root, stack);
        }

        graph.TotalSamples = totalSamples;
        graph.TotalTimeMs = totalSamples; // Assuming duration in ms
        graph.CalculatePercentages();

        _logger.LogInformation(
            "Built flame graph with {Samples} samples for {CorrelationId}",
            totalSamples, correlationId);

        return graph;
    }

    /// <summary>
    /// Builds a memory allocation flame graph
    /// </summary>
    public async ValueTask<FlameGraph> BuildMemoryFlameGraphAsync(string correlationId)
    {
        _logger.LogInformation("Building memory flame graph for {CorrelationId}", correlationId);

        var events = await _eventStore.GetEventsByCorrelationAsync(correlationId);
        var root = new FlameGraphNode("root", "root");
        var graph = new FlameGraph(root, FlameGraphType.Memory)
        {
            CorrelationId = correlationId
        };

        // TODO: Extract memory allocation data from events
        // For now, use duration as a proxy
        var callStacks = ExtractCallStacks(events);
        long totalAllocations = 0;

        foreach (var stack in callStacks)
        {
            totalAllocations += stack.Duration / 10; // Simplified
            AddStackToGraph(root, stack, useMemory: true);
        }

        graph.TotalSamples = totalAllocations;
        graph.TotalTimeMs = totalAllocations;
        graph.CalculatePercentages();

        return graph;
    }

    private List<CallStackSample> ExtractCallStacks(IEnumerable<Models.ReplayableEvent> events)
    {
        var samples = new List<CallStackSample>();

        foreach (var evt in events)
        {
            // Extract call stack from event data
            // For now, create a simple sample from event type
            samples.Add(new CallStackSample
            {
                Frames = new List<string> { evt.Type.ToString() },
                Duration = 100 // Simplified
            });
        }

        return samples;
    }

    private void AddStackToGraph(FlameGraphNode root, CallStackSample stack, bool useMemory = false)
    {
        var currentNode = root;

        foreach (var frame in stack.Frames)
        {
            // Find or create child node
            var childNode = currentNode.Children.FirstOrDefault(c => c.FullName == frame);
            if (childNode == null)
            {
                var name = frame.Split('.').LastOrDefault() ?? frame;
                childNode = currentNode.AddChild(name, frame);
            }

            childNode.SampleCount++;
            childNode.TotalTime += stack.Duration;

            currentNode = childNode;
        }

        // Add duration to self time of leaf node
        currentNode.SelfTime += stack.Duration;
    }

    private sealed class CallStackSample
    {
        public List<string> Frames { get; set; } = new();
        public long Duration { get; set; }
    }
}

