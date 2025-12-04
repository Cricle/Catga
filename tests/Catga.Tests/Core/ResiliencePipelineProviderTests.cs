using Catga.Resilience;
using FluentAssertions;
using Polly.Timeout;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for DefaultResiliencePipelineProvider after refactoring.
/// </summary>
public class ResiliencePipelineProviderTests
{
    [Fact]
    public async Task ExecuteMediatorAsync_WithResult_ShouldReturnValue()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var expected = 42;

        // Act
        var result = await provider.ExecuteMediatorAsync(async ct =>
        {
            await Task.Delay(1, ct);
            return expected;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteMediatorAsync_WithoutResult_ShouldComplete()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        // Act
        await provider.ExecuteMediatorAsync(async ct =>
        {
            await Task.Delay(1, ct);
            executed = true;
        }, CancellationToken.None);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTransportPublishAsync_WithResult_ShouldReturnValue()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var expected = "published";

        // Act
        var result = await provider.ExecuteTransportPublishAsync(async ct =>
        {
            await Task.Delay(1, ct);
            return expected;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteTransportPublishAsync_WithoutResult_ShouldComplete()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        // Act
        await provider.ExecuteTransportPublishAsync(async ct =>
        {
            await Task.Delay(1, ct);
            executed = true;
        }, CancellationToken.None);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTransportSendAsync_WithResult_ShouldReturnValue()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var expected = 123L;

        // Act
        var result = await provider.ExecuteTransportSendAsync(async ct =>
        {
            await Task.Delay(1, ct);
            return expected;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteTransportSendAsync_WithoutResult_ShouldComplete()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        // Act
        await provider.ExecuteTransportSendAsync(async ct =>
        {
            await Task.Delay(1, ct);
            executed = true;
        }, CancellationToken.None);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutePersistenceAsync_WithResult_ShouldReturnValue()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var expected = new { Id = 1, Name = "test" };

        // Act
        var result = await provider.ExecutePersistenceAsync(async ct =>
        {
            await Task.Delay(1, ct);
            return expected;
        }, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ExecutePersistenceAsync_WithoutResult_ShouldComplete()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        // Act
        await provider.ExecutePersistenceAsync(async ct =>
        {
            await Task.Delay(1, ct);
            executed = true;
        }, CancellationToken.None);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutePersistenceAsync_WithRetry_ShouldRetryOnFailure()
    {
        // Arrange
        var options = new CatgaResilienceOptions
        {
            PersistenceRetryCount = 3,
            PersistenceRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var provider = new DefaultResiliencePipelineProvider(options);
        var attempts = 0;

        // Act
        var result = await provider.ExecutePersistenceAsync(async ct =>
        {
            attempts++;
            if (attempts < 3)
                throw new InvalidOperationException("Transient failure");
            await Task.Delay(1, ct);
            return attempts;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(3);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteTransportPublishAsync_WithRetry_ShouldRetryOnFailure()
    {
        // Arrange
        var options = new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var provider = new DefaultResiliencePipelineProvider(options);
        var attempts = 0;

        // Act
        var result = await provider.ExecuteTransportPublishAsync(async ct =>
        {
            attempts++;
            if (attempts < 2)
                throw new InvalidOperationException("Transient failure");
            await Task.Delay(1, ct);
            return attempts;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(2);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task DefaultOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange
        var options = new CatgaResilienceOptions();

        // Assert
        options.MediatorTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        options.TransportTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        options.PersistenceTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        options.TransportRetryCount.Should().BeGreaterOrEqualTo(0);
        options.PersistenceRetryCount.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task CustomOptions_ShouldBeApplied()
    {
        // Arrange
        var options = new CatgaResilienceOptions
        {
            MediatorTimeout = TimeSpan.FromSeconds(5),
            MediatorBulkheadConcurrency = 50,
            TransportTimeout = TimeSpan.FromSeconds(10),
            TransportRetryCount = 5,
            PersistenceTimeout = TimeSpan.FromSeconds(15),
            PersistenceRetryCount = 3
        };
        var provider = new DefaultResiliencePipelineProvider(options);
        var executed = false;

        // Act
        await provider.ExecuteMediatorAsync(async ct =>
        {
            await Task.Delay(1, ct);
            executed = true;
        }, CancellationToken.None);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task NullOptions_ShouldUseDefaults()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider(null);
        var executed = false;

        // Act
        await provider.ExecuteMediatorAsync(async ct =>
        {
            await Task.Delay(1, ct);
            executed = true;
        }, CancellationToken.None);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationToken_ShouldBePropagated()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await provider.ExecuteMediatorAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1000, ct);
                return 1;
            }, cts.Token);
        });
    }
}
