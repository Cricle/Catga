using BenchmarkDotNet.Attributes;
using Catga.Debugging;
using Catga.Messages;
using Catga.Results;

namespace Catga.Benchmarks;

/// <summary>
/// Debug feature performance benchmarks - verify less than 0.5us overhead
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class DebugBenchmarks
{
    private MessageFlowTracker _tracker = null!;
    private DebugOptions _debugEnabled = null!;
    private DebugOptions _debugDisabled = null!;
    private FlowContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _debugEnabled = new DebugOptions { EnableDebug = true, MaxActiveFlows = 1000 };
        _debugDisabled = new DebugOptions { EnableDebug = false };
        _tracker = new MessageFlowTracker(_debugEnabled);
        
        // Pre-create context for reuse tests
        _context = _tracker.BeginFlow("test-flow", "TestCommand");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _tracker?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void NoDebug_FastPath()
    {
        // Simulates handler execution without debug
        if (_debugDisabled.EnableDebug)
        {
            // Would do debug tracking
        }
        // Fast path - just execute
    }

    [Benchmark]
    public void Debug_BeginFlow()
    {
        var context = _tracker.BeginFlow($"flow-{Guid.NewGuid():N}", "TestCommand");
        _tracker.EndFlow(context.CorrelationId);
    }

    [Benchmark]
    public void Debug_RecordStep()
    {
        _tracker.RecordStep(_context.CorrelationId, new StepInfo(
            "Handler",
            "TestHandler",
            TimeSpan.FromMilliseconds(1),
            true
        ));
    }

    [Benchmark]
    public void Debug_EndFlow()
    {
        var context = _tracker.BeginFlow($"flow-{Guid.NewGuid():N}", "TestCommand");
        _tracker.RecordStep(context.CorrelationId, new StepInfo("Handler", "Test", TimeSpan.FromMilliseconds(1), true));
        var summary = _tracker.EndFlow(context.CorrelationId);
    }

    [Benchmark]
    public void Debug_GetActiveFlows()
    {
        var flows = _tracker.GetActiveFlows();
    }

    [Benchmark]
    public void Debug_GetStatistics()
    {
        var stats = _tracker.GetStatistics();
    }

    [Benchmark]
    public void ConsoleFormatter_FormatCompact()
    {
        var summary = new FlowSummary
        {
            CorrelationId = "abc123def456",
            MessageType = "TestCommand",
            TotalDuration = TimeSpan.FromMilliseconds(0.8),
            Success = true
        };

        var formatted = ConsoleFlowFormatter.FormatCompact(summary);
    }

    [Benchmark]
    public void ConsoleFormatter_FormatTree()
    {
        var context = new FlowContext
        {
            CorrelationId = "abc123",
            MessageType = "TestCommand",
            StartTime = DateTime.UtcNow,
            TraceId = "xyz789"
        };
        
        context.Steps.Add(new StepInfo("Handler", "TestHandler", TimeSpan.FromMilliseconds(1), true));
        context.Steps.Add(new StepInfo("Repository", "SaveAsync", TimeSpan.FromMilliseconds(0.5), true));
        
        var formatted = ConsoleFlowFormatter.FormatTree(context);
    }
}

