using Catga.Pipeline;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Unit tests for MediatorBatch components.
/// </summary>
public class MediatorBatchTests
{
    [Fact]
    public void MediatorBatchOptions_DefaultValues()
    {
        // Act
        var options = new MediatorBatchOptions();

        // Assert
        options.MaxBatchSize.Should().Be(100);
        options.BatchTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        options.EnableAutoBatching.Should().BeFalse();
        options.MaxQueueLength.Should().Be(10_000);
    }

    [Fact]
    public void MediatorBatchOptions_CustomValues()
    {
        // Act
        var options = new MediatorBatchOptions
        {
            MaxBatchSize = 500,
            BatchTimeout = TimeSpan.FromSeconds(2),
            EnableAutoBatching = true,
            MaxShards = 1024
        };

        // Assert
        options.MaxBatchSize.Should().Be(500);
        options.BatchTimeout.Should().Be(TimeSpan.FromSeconds(2));
        options.EnableAutoBatching.Should().BeTrue();
        options.MaxShards.Should().Be(1024);
    }

    [Fact]
    public void MediatorBatchProfiles_RegisterOptionsTransformer_TransformsOptions()
    {
        // Arrange - clear any previous state
        MediatorBatchProfiles<TestRequest>.OptionsTransformers = null;

        // Act
        MediatorBatchProfiles.RegisterOptionsTransformer<TestRequest>(opts => opts with { MaxBatchSize = 200 });

        // Assert
        var transformer = MediatorBatchProfiles<TestRequest>.OptionsTransformers;
        transformer.Should().NotBeNull();
        var result = transformer!(new MediatorBatchOptions());
        result.MaxBatchSize.Should().Be(200);
    }

    [Fact]
    public void MediatorBatchProfiles_RegisterKeySelector_SetsSelector()
    {
        // Arrange
        MediatorBatchProfiles<TestRequest>.KeySelector = null;

        // Act
        MediatorBatchProfiles.RegisterKeySelector<TestRequest>(r => r.Key);

        // Assert
        var selector = MediatorBatchProfiles<TestRequest>.KeySelector;
        selector.Should().NotBeNull();
        selector!(new TestRequest("test-key")).Should().Be("test-key");
    }

    [Fact]
    public void MediatorBatchProfiles_ChainedTransformers_ApplyInOrder()
    {
        // Arrange
        MediatorBatchProfiles<TestRequest>.OptionsTransformers = null;

        // Act - register two transformers
        MediatorBatchProfiles.RegisterOptionsTransformer<TestRequest>(opts => opts with { MaxBatchSize = 50 });
        MediatorBatchProfiles.RegisterOptionsTransformer<TestRequest>(opts => opts with { MaxShards = 512 });

        // Assert
        var transformer = MediatorBatchProfiles<TestRequest>.OptionsTransformers;
        var result = transformer!(new MediatorBatchOptions());
        result.MaxBatchSize.Should().Be(50);
        result.MaxShards.Should().Be(512);
    }

    private record TestRequest(string Key);
}






