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
    /// Builds a memory allocation flame graph using real MemoryAllocated data
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

        // Extract call stacks with memory allocation data
        var callStacks = ExtractCallStacks(events);
        long totalAllocations = 0;

        foreach (var stack in callStacks)
        {
            totalAllocations += stack.MemoryAllocated;
            AddStackToGraph(root, stack, useMemory: true);
        }

        graph.TotalSamples = totalAllocations;
        graph.TotalTimeMs = totalAllocations; // For memory, this represents total bytes
        graph.CalculatePercentages();

        _logger.LogInformation(
            "Built memory flame graph with {TotalBytes} bytes allocated for {CorrelationId}",
            totalAllocations, correlationId);

        return graph;
    }

    private List<CallStackSample> ExtractCallStacks(IEnumerable<Models.ReplayableEvent> events)
    {
        var samples = new List<CallStackSample>();

        foreach (var evt in events)
        {
            var frames = new List<string>();
            long duration = (long)evt.Duration;
            long memory = evt.MemoryAllocated ?? 0;

            // Try to extract call stack from StateSnapshot data
            if (evt.Data is Models.StateSnapshot snapshot && snapshot.CallStack != null && snapshot.CallStack.Any())
            {
                // Build frame list from call stack (bottom to top for flame graph)
                foreach (var frame in snapshot.CallStack)
                {
                    var frameName = frame.MethodName;
                    if (!string.IsNullOrEmpty(frame.FileName))
                    {
                        frameName = $"{frame.MethodName} ({frame.FileName}:{frame.LineNumber})";
                    }
                    frames.Add(frameName);
                }
            }
            else
            {
                // Fallback: use message type as single frame
                var messageType = evt.MessageType ?? evt.Type.ToString();
                frames.Add(messageType);
            }

            samples.Add(new CallStackSample
            {
                Frames = frames,
                Duration = duration,
                MemoryAllocated = memory
            });
        }

        _logger.LogInformation("Extracted {Count} call stack samples from {EventCount} events", 
            samples.Count, 
            events.Count());

        return samples;
    }

    private void AddStackToGraph(FlameGraphNode root, CallStackSample stack, bool useMemory = false)
    {
        var currentNode = root;
        var value = useMemory ? stack.MemoryAllocated : stack.Duration;

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
            childNode.TotalTime += value;

            currentNode = childNode;
        }

        // Add value to self time of leaf node
        currentNode.SelfTime += value;
    }

    private sealed class CallStackSample
    {
        public List<string> Frames { get; set; } = new();
        public long Duration { get; set; }
        public long MemoryAllocated { get; set; }
    }
}

