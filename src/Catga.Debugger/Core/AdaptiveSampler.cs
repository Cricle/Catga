using System.Diagnostics;
using System.Runtime.CompilerServices;
using Catga.Debugger.Models;

namespace Catga.Debugger.Core;

/// <summary>Adaptive sampler - dynamically adjusts sampling rate based on system load</summary>
public sealed class AdaptiveSampler
{
    private readonly ReplayOptions _options;
    private double _currentRate;
    private readonly double _minRate;
    private readonly double _maxRate;
    private DateTime _lastAdjustment = DateTime.UtcNow;
    private readonly TimeSpan _adjustmentInterval = TimeSpan.FromSeconds(10);

    public AdaptiveSampler(ReplayOptions options)
    {
        _options = options;
        _currentRate = options.SamplingRate;
        _minRate = options.Mode == DebuggerMode.ProductionOptimized ? 0.0001 : 0.001;
        _maxRate = options.Mode == DebuggerMode.Development ? 1.0 : 0.01;
    }

    /// <summary>Determine if current request should be sampled</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldSample(string? identifier = null)
    {
        if (!_options.EnableReplay) return false;

        // Always sample in development mode
        if (_options.Mode == DebuggerMode.Development && _options.SamplingRate >= 1.0)
            return true;

        // Adaptive sampling adjustment (periodic)
        if (_options.EnableAdaptiveSampling &&
            DateTime.UtcNow - _lastAdjustment > _adjustmentInterval)
        {
            AdjustRateBasedOnLoad();
            _lastAdjustment = DateTime.UtcNow;
        }

        // Sampling decision
        return _options.SamplingStrategy switch
        {
            SamplingStrategy.HashBased => HashBasedSample(identifier),
            SamplingStrategy.Random => RandomSample(),
            SamplingStrategy.Adaptive => AdaptiveSample(),
            _ => RandomSample()
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HashBasedSample(string? identifier)
    {
        if (identifier == null) return RandomSample();

        // Deterministic sampling based on hash
        var hash = identifier.GetHashCode();
        var normalizedHash = Math.Abs(hash % 10000) / 10000.0;
        return normalizedHash < _currentRate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool RandomSample()
    {
        return Random.Shared.NextDouble() < _currentRate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdaptiveSample()
    {
        // Use hash-based + adaptive rate
        return Random.Shared.NextDouble() < _currentRate;
    }

    private void AdjustRateBasedOnLoad()
    {
        var cpuUsage = GetCpuUsage();
        var memoryUsage = GetMemoryUsage();

        // High load - reduce sampling
        if (cpuUsage > 0.8 || memoryUsage > 0.8)
        {
            _currentRate = Math.Max(_minRate, _currentRate * 0.5);
        }
        // Low load - increase sampling
        else if (cpuUsage < 0.3 && memoryUsage < 0.5)
        {
            _currentRate = Math.Min(_maxRate, _currentRate * 1.2);
        }
        // Medium load - slight decrease
        else if (cpuUsage > 0.6 || memoryUsage > 0.6)
        {
            _currentRate = Math.Max(_minRate, _currentRate * 0.9);
        }
    }

    private double GetCpuUsage()
    {
        // Simplified CPU usage estimation
        // In production, use Process.GetCurrentProcess().TotalProcessorTime
        var process = Process.GetCurrentProcess();
        return Math.Min(1.0, process.TotalProcessorTime.TotalMilliseconds /
            (Environment.ProcessorCount * process.TotalProcessorTime.TotalMilliseconds + 1));
    }

    private double GetMemoryUsage()
    {
        var process = Process.GetCurrentProcess();
        var usedMemory = process.WorkingSet64;
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        return totalMemory > 0 ? (double)usedMemory / totalMemory : 0.0;
    }

    /// <summary>Get current sampling rate</summary>
    public double CurrentRate => _currentRate;
}

