using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Catga.Abstractions;
using FluentAssertions;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Performance;

/// <summary>
/// Performance benchmarks comparing source-generated vs reflection-based registration.
/// </summary>
public class FlowDslRegistrationBenchmarks
{
    private readonly ITestOutputHelper _output;

    public FlowDslRegistrationBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SourceGenerated_Registration_IsFasterThanReflection()
    {
        const int iterations = 1000;

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            RegisterWithSourceGeneration();
            RegisterWithReflection();
        }

        // Measure source generation
        var sourceGenStopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            RegisterWithSourceGeneration();
        }
        sourceGenStopwatch.Stop();

        // Measure reflection
        var reflectionStopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            RegisterWithReflection();
        }
        reflectionStopwatch.Stop();

        // Output results
        _output.WriteLine($"Source Generation: {sourceGenStopwatch.ElapsedMilliseconds}ms for {iterations} iterations");
        _output.WriteLine($"Reflection: {reflectionStopwatch.ElapsedMilliseconds}ms for {iterations} iterations");
        _output.WriteLine($"Speed improvement: {(double)reflectionStopwatch.ElapsedMilliseconds / sourceGenStopwatch.ElapsedMilliseconds:F2}x");

        // Assert
        sourceGenStopwatch.ElapsedMilliseconds.Should().BeLessThan(
            reflectionStopwatch.ElapsedMilliseconds,
            "Source generation should be faster than reflection");

        // Source generation should be at least 5x faster
        var speedup = (double)reflectionStopwatch.ElapsedMilliseconds / sourceGenStopwatch.ElapsedMilliseconds;
        speedup.Should().BeGreaterThan(5.0, "Source generation should be at least 5x faster");
    }

    [Fact]
    public void SourceGenerated_Registration_UsesLessMemory()
    {
        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeMemory = GC.GetTotalMemory(true);

        // Register with source generation 100 times
        for (int i = 0; i < 100; i++)
        {
            RegisterWithSourceGeneration();
        }

        var sourceGenMemory = GC.GetTotalMemory(true) - beforeMemory;

        // Reset
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        beforeMemory = GC.GetTotalMemory(true);

        // Register with reflection 100 times
        for (int i = 0; i < 100; i++)
        {
            RegisterWithReflection();
        }

        var reflectionMemory = GC.GetTotalMemory(true) - beforeMemory;

        // Output results
        _output.WriteLine($"Source Generation memory: {sourceGenMemory / 1024}KB");
        _output.WriteLine($"Reflection memory: {reflectionMemory / 1024}KB");
        _output.WriteLine($"Memory savings: {(1 - (double)sourceGenMemory / reflectionMemory) * 100:F1}%");

        // Assert
        sourceGenMemory.Should().BeLessThan(
            reflectionMemory,
            "Source generation should use less memory than reflection");
    }

    [Fact]
    public void SourceGenerated_GetRegisteredFlows_IsInstant()
    {
        const int iterations = 10000;

        // Warm up
        for (int i = 0; i < 100; i++)
        {
            _ = CatgaGeneratedFlowRegistrations.GetRegisteredFlows();
        }

        // Measure
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var flows = CatgaGeneratedFlowRegistrations.GetRegisteredFlows();
            _ = flows.Count; // Ensure it's evaluated
        }
        stopwatch.Stop();

        var avgMicroseconds = stopwatch.Elapsed.TotalMilliseconds * 1000 / iterations;

        _output.WriteLine($"GetRegisteredFlows average: {avgMicroseconds:F2}μs per call");
        _output.WriteLine($"Total time for {iterations} calls: {stopwatch.ElapsedMilliseconds}ms");

        // Should be very fast - less than 1 microsecond per call
        avgMicroseconds.Should().BeLessThan(1.0, "Getting registered flows should be instant");
    }

    [Fact]
    public async Task SourceGenerated_FlowExecution_HasNoReflectionOverhead()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);

        // Setup mediator
        mediator.SendAsync<BenchmarkCommand, string>(Arg.Any<BenchmarkCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("result")));

        // Register with source generation
        services.AddFlowDsl();
        services.AddFlow<BenchmarkState, BenchmarkFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<BenchmarkState, BenchmarkFlow>>();

        const int iterations = 1000;

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            var warmupState = new BenchmarkState { FlowId = $"warmup-{i}" };
            await executor!.RunAsync(warmupState);
        }

        // Measure execution
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var state = new BenchmarkState { FlowId = $"benchmark-{i}" };
            await executor!.RunAsync(state);
        }
        stopwatch.Stop();

        var avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine($"Average flow execution: {avgMs:F3}ms");
        _output.WriteLine($"Throughput: {iterations / stopwatch.Elapsed.TotalSeconds:F0} flows/second");

        // Should be very fast - less than 1ms per flow
        avgMs.Should().BeLessThan(1.0, "Flow execution should have minimal overhead");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void SourceGenerated_Registration_ScalesLinearly(int flowCount)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());

        var stopwatch = Stopwatch.StartNew();

        // Register all generated flows
        services.AddGeneratedFlows();

        // Register additional flows manually
        for (int i = 0; i < flowCount; i++)
        {
            // This uses compile-time generated code, not reflection
            if (i % 3 == 0)
                services.AddFlow<BenchmarkState, BenchmarkFlow>();
            else if (i % 3 == 1)
                services.AddFlow<AnotherBenchmarkState, AnotherBenchmarkFlow>();
            else
                services.AddFlow<ThirdBenchmarkState, ThirdBenchmarkFlow>();
        }

        stopwatch.Stop();

        _output.WriteLine($"Registration of {flowCount} flows: {stopwatch.ElapsedMilliseconds}ms");

        // Should scale linearly - approximately 0.1ms per flow or less
        var msPerFlow = stopwatch.Elapsed.TotalMilliseconds / flowCount;
        msPerFlow.Should().BeLessThan(0.1, "Registration should be very fast per flow");
    }

    [Fact]
    public void SourceGenerated_FirstTimeStartup_IsFast()
    {
        // Simulate cold start
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());

        var stopwatch = Stopwatch.StartNew();

        // First time registration
        services.AddFlowDsl(options =>
        {
            options.AutoRegisterFlows = true;
        });

        // Build provider (triggers any lazy initialization)
        var provider = services.BuildServiceProvider();

        // Get first service (might trigger additional initialization)
        var store = provider.GetService<IDslFlowStore>();

        stopwatch.Stop();

        _output.WriteLine($"Cold start time: {stopwatch.ElapsedMilliseconds}ms");

        // Cold start should be fast - less than 50ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "Cold start should be fast");
    }

    // Helper methods
    private void RegisterWithSourceGeneration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());
        services.AddGeneratedFlows(); // Source-generated method
        _ = services.BuildServiceProvider();
    }

    private void RegisterWithReflection()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());

        // Simulate reflection-based registration
        var assembly = Assembly.GetExecutingAssembly();
        var flowTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract &&
                       !t.IsInterface &&
                       t.BaseType != null &&
                       t.BaseType.IsGenericType &&
                       t.BaseType.GetGenericTypeDefinition() == typeof(FlowConfig<>))
            .ToList();

        foreach (var flowType in flowTypes)
        {
            var stateType = flowType.BaseType!.GetGenericArguments()[0];
            var baseType = typeof(FlowConfig<>).MakeGenericType(stateType);
            services.AddScoped(baseType, flowType);
            services.AddScoped(flowType);
        }

        _ = services.BuildServiceProvider();
    }
}

// Benchmark flow configurations
public class BenchmarkFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.Name("benchmark-flow");
        flow.Send(s => new BenchmarkCommand { Value = s.Value });
    }
}

public class AnotherBenchmarkFlow : FlowConfig<AnotherBenchmarkState>
{
    protected override void Configure(IFlowBuilder<AnotherBenchmarkState> flow)
    {
        flow.Name("another-benchmark-flow");
        flow.Send(s => new BenchmarkCommand { Value = 42 });
    }
}

public class ThirdBenchmarkFlow : FlowConfig<ThirdBenchmarkState>
{
    protected override void Configure(IFlowBuilder<ThirdBenchmarkState> flow)
    {
        flow.Name("third-benchmark-flow");
        flow.Send(s => new BenchmarkCommand { Value = 100 });
    }
}

// Benchmark states
public class BenchmarkState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class AnotherBenchmarkState : IFlowState
{
    public string? FlowId { get; set; }
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ThirdBenchmarkState : IFlowState
{
    public string? FlowId { get; set; }
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Benchmark command
public record BenchmarkCommand : IRequest<string>
{
    public int Value { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
