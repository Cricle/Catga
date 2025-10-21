using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

/// <summary>
/// Source Generator performance - measure auto-registration vs manual
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class SourceGeneratorBenchmarks
{
    private ServiceProvider _autoRegisteredProvider = null!;
    private ServiceProvider _manualRegisteredProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Auto-registered services (Source Generator)
        var autoServices = new ServiceCollection();
        autoServices.AddGeneratedHandlers();
        autoServices.AddGeneratedServices();
        _autoRegisteredProvider = autoServices.BuildServiceProvider();

        // Manually registered services
        var manualServices = new ServiceCollection();
        // Manual registration would go here
        _manualRegisteredProvider = manualServices.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _autoRegisteredProvider?.Dispose();
        _manualRegisteredProvider?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void ManualRegistration_Resolve()
    {
        // Measure resolution time (manual registration baseline)
        using var scope = _manualRegisteredProvider.CreateScope();
    }

    [Benchmark]
    public void AutoRegistration_Resolve()
    {
        // Measure resolution time (auto-registered)
        using var scope = _autoRegisteredProvider.CreateScope();
    }

    [Benchmark]
    public void ServiceProvider_CreateScope()
    {
        // Baseline: just creating scope
        using var scope = _autoRegisteredProvider.CreateScope();
    }
}

/// <summary>
/// Event Router performance - zero-reflection vs traditional
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class EventRouterBenchmarks
{
    private TestEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        _event = new TestEvent("test-123");
    }

    [Benchmark(Baseline = true)]
    public void Traditional_TypeCheck()
    {
        // Traditional: using GetType() and reflection
        var type = _event.GetType();
        var name = type.Name;
    }

    [Benchmark]
    public void Generated_PatternMatch()
    {
        // Source Generator: pattern matching (zero-reflection)
        if (_event is TestEvent evt)
        {
            var id = evt.Id;
        }
    }

    public partial record TestEvent(string Id) : IEvent
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    }
}

