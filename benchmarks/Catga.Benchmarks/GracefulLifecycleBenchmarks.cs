using BenchmarkDotNet.Attributes;
using Catga.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catga.Benchmarks;

/// <summary>
/// Graceful shutdown and recovery performance benchmarks
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class GracefulLifecycleBenchmarks
{
    private GracefulShutdownManager _shutdownManager = null!;
    private GracefulRecoveryManager _recoveryManager = null!;
    private MockRecoverableComponent _component = null!;

    [GlobalSetup]
    public void Setup()
    {
        _shutdownManager = new GracefulShutdownManager(NullLogger<GracefulShutdownManager>.Instance);
        _recoveryManager = new GracefulRecoveryManager(NullLogger<GracefulRecoveryManager>.Instance);
        _component = new MockRecoverableComponent();
        _recoveryManager.RegisterComponent(_component);
    }

    [Benchmark]
    public void BeginOperation()
    {
        using var scope = _shutdownManager.BeginOperation();
    }

    [Benchmark]
    public void BeginOperation_10x()
    {
        for (int i = 0; i < 10; i++)
        {
            using var scope = _shutdownManager.BeginOperation();
        }
    }

    [Benchmark]
    public void RegisterComponent()
    {
        var component = new MockRecoverableComponent();
        _recoveryManager.RegisterComponent(component);  // Lock-free with ConcurrentBag
    }

    [Benchmark]
    public void CheckShutdownStatus()
    {
        var isShuttingDown = _shutdownManager.IsShuttingDown;
        var activeOps = _shutdownManager.ActiveOperations;
    }

    [Benchmark]
    public async Task RecoverComponent()
    {
        await _component.RecoverAsync();
    }

    private class MockRecoverableComponent : IRecoverableComponent
    {
        public bool IsHealthy => true;

        public Task RecoverAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

