using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Matrix test to verify feature parity across all storage implementations.
/// This ensures that all IDslFlowStore implementations support the same features.
/// </summary>
public class StorageFeatureParityMatrix
{
    [Fact]
    public void AllStorageImplementations_ShouldImplementForEachMethods()
    {
        // Verify that all storage implementations have ForEach methods
        var storeInterface = typeof(IDslFlowStore);
        var forEachMethods = storeInterface.GetMethods()
            .Where(m => m.Name.Contains("ForEach"))
            .ToList();

        // Should have exactly 3 ForEach methods
        forEachMethods.Should().HaveCount(3);
        forEachMethods.Should().Contain(m => m.Name == "SaveForEachProgressAsync");
        forEachMethods.Should().Contain(m => m.Name == "GetForEachProgressAsync");
        forEachMethods.Should().Contain(m => m.Name == "ClearForEachProgressAsync");

        // Verify method signatures
        var saveMethod = forEachMethods.First(m => m.Name == "SaveForEachProgressAsync");
        saveMethod.GetParameters().Should().HaveCount(4); // flowId, stepIndex, progress, ct
        saveMethod.GetParameters()[0].ParameterType.Should().Be<string>();
        saveMethod.GetParameters()[1].ParameterType.Should().Be<int>();
        saveMethod.GetParameters()[2].ParameterType.Should().Be<ForEachProgress>();

        var getMethod = forEachMethods.First(m => m.Name == "GetForEachProgressAsync");
        getMethod.GetParameters().Should().HaveCount(3); // flowId, stepIndex, ct
        getMethod.ReturnType.Should().Be(typeof(Task<ForEachProgress?>));

        var clearMethod = forEachMethods.First(m => m.Name == "ClearForEachProgressAsync");
        clearMethod.GetParameters().Should().HaveCount(3); // flowId, stepIndex, ct
        clearMethod.ReturnType.Should().Be<Task>();
    }

    [Theory]
    [InlineData(typeof(Catga.Flow.Dsl.InMemoryDslFlowStore))]
    [InlineData(typeof(Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore))]
    [InlineData(typeof(Catga.Persistence.Redis.Flow.RedisDslFlowStore))]
    [InlineData(typeof(Catga.Persistence.Nats.Flow.NatsDslFlowStore))]
    public void StorageImplementation_ShouldImplementAllForEachMethods(Type storeType)
    {
        // Verify each storage implementation has all required ForEach methods
        var methods = storeType.GetMethods();

        methods.Should().Contain(m => m.Name == "SaveForEachProgressAsync");
        methods.Should().Contain(m => m.Name == "GetForEachProgressAsync");
        methods.Should().Contain(m => m.Name == "ClearForEachProgressAsync");

        // Verify the class implements IDslFlowStore
        storeType.GetInterfaces().Should().Contain(typeof(IDslFlowStore));
    }

    [Fact]
    public void ForEachProgress_ShouldHaveConsistentStructure()
    {
        // Verify ForEachProgress record has expected properties
        var progressType = typeof(ForEachProgress);

        var properties = progressType.GetProperties();
        properties.Should().Contain(p => p.Name == "CurrentIndex" && p.PropertyType == typeof(int));
        properties.Should().Contain(p => p.Name == "TotalCount" && p.PropertyType == typeof(int));
        properties.Should().Contain(p => p.Name == "CompletedIndices" && p.PropertyType == typeof(List<int>));
        properties.Should().Contain(p => p.Name == "FailedIndices" && p.PropertyType == typeof(List<int>));

        // Verify it's a record (has with expressions support)
        progressType.IsClass.Should().BeTrue();
        progressType.GetMethod("<Clone>$").Should().NotBeNull(); // Records have clone method
    }

    [Fact]
    public void StepType_ShouldIncludeForEach()
    {
        // Verify StepType enum includes ForEach
        var stepTypeValues = Enum.GetValues<StepType>();
        stepTypeValues.Should().Contain(StepType.ForEach);
    }

    [Fact]
    public void ForEachFailureHandling_ShouldHaveExpectedValues()
    {
        // Verify ForEachFailureHandling enum has expected values
        var values = Enum.GetValues<ForEachFailureHandling>();
        values.Should().Contain(ForEachFailureHandling.StopOnFirstFailure);
        values.Should().Contain(ForEachFailureHandling.ContinueOnFailure);
        values.Should().HaveCount(2);
    }

    [Fact]
    public void FlowStep_ShouldHaveForEachProperties()
    {
        // Verify FlowStep has key ForEach-related properties
        var stepType = typeof(FlowStep);
        var publicProperties = stepType.GetProperties();

        // Public properties that are essential for ForEach
        publicProperties.Should().Contain(p => p.Name == "ItemSteps");
        publicProperties.Should().Contain(p => p.Name == "BatchSize" && p.PropertyType == typeof(int));
        publicProperties.Should().Contain(p => p.Name == "FailureHandling" && p.PropertyType == typeof(ForEachFailureHandling));

        // Verify the class has the Type property that can be set to ForEach
        publicProperties.Should().Contain(p => p.Name == "Type" && p.PropertyType == typeof(StepType));
    }
}
