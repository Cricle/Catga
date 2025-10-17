namespace Catga.Debugger.Models;

/// <summary>Debugger operation mode</summary>
public enum DebuggerMode
{
    /// <summary>Development - full capture, no sampling</summary>
    Development,

    /// <summary>Staging - moderate sampling</summary>
    Staging,

    /// <summary>Production - minimal overhead, adaptive sampling</summary>
    ProductionOptimized
}

/// <summary>Sampling strategy</summary>
public enum SamplingStrategy
{
    /// <summary>Random sampling</summary>
    Random,

    /// <summary>Hash-based deterministic sampling</summary>
    HashBased,

    /// <summary>Adaptive based on system load</summary>
    Adaptive
}

/// <summary>Overflow handling strategy</summary>
public enum OverflowStrategy
{
    /// <summary>Drop oldest events</summary>
    DropOldest,

    /// <summary>Drop newest events</summary>
    DropNewest,

    /// <summary>Block until space available (not recommended)</summary>
    Block
}

/// <summary>Replay configuration options</summary>
public sealed class ReplayOptions
{
    /// <summary>Enable replay capture</summary>
    public bool EnableReplay { get; set; } = true;

    /// <summary>Debugger operation mode</summary>
    public DebuggerMode Mode { get; set; } = DebuggerMode.Development;

    /// <summary>Sampling rate (0.0-1.0)</summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>Enable adaptive sampling</summary>
    public bool EnableAdaptiveSampling { get; set; }

    /// <summary>Sampling strategy</summary>
    public SamplingStrategy SamplingStrategy { get; set; } = SamplingStrategy.HashBased;

    /// <summary>Maximum memory usage in MB</summary>
    public int MaxMemoryMB { get; set; } = 100;

    /// <summary>Use ring buffer (fixed memory)</summary>
    public bool UseRingBuffer { get; set; } = true;

    /// <summary>Ring buffer capacity</summary>
    public int RingBufferCapacity { get; set; } = 1000;

    /// <summary>Enable zero-copy optimizations</summary>
    public bool EnableZeroCopy { get; set; } = true;

    /// <summary>Enable object pooling</summary>
    public bool EnableObjectPooling { get; set; } = true;

    /// <summary>Batch size for processing</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Batch processing interval</summary>
    public TimeSpan BatchInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Track message flows</summary>
    public bool TrackMessageFlows { get; set; } = true;

    /// <summary>Track performance metrics</summary>
    public bool TrackPerformance { get; set; } = true;

    /// <summary>Track state snapshots</summary>
    public bool TrackStateSnapshots { get; set; } = true;

    /// <summary>Track exceptions</summary>
    public bool TrackExceptions { get; set; } = true;

    /// <summary>Capture variable values</summary>
    public bool CaptureVariables { get; set; } = true;

    /// <summary>Capture call stacks</summary>
    public bool CaptureCallStacks { get; set; } = true;

    /// <summary>Capture memory state</summary>
    public bool CaptureMemoryState { get; set; }

    /// <summary>Enable backpressure control</summary>
    public bool EnableBackpressure { get; set; } = true;

    /// <summary>Backpressure threshold</summary>
    public int BackpressureThreshold { get; set; } = 10000;

    /// <summary>Overflow strategy</summary>
    public OverflowStrategy OverflowStrategy { get; set; } = OverflowStrategy.DropOldest;

    /// <summary>Enable compression</summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>Auto-disable after duration (production safety)</summary>
    public TimeSpan? AutoDisableAfter { get; set; }

    /// <summary>Allow manual enable/disable at runtime</summary>
    public bool AllowManualEnable { get; set; } = true;

    /// <summary>Event retention duration</summary>
    public TimeSpan EventRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Snapshot retention duration</summary>
    public TimeSpan SnapshotRetention { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Service/component name for identification</summary>
    public string? ServiceName { get; set; }

    /// <summary>Slow query threshold in milliseconds</summary>
    public double SlowQueryThresholdMs { get; set; } = 1000.0;
}

