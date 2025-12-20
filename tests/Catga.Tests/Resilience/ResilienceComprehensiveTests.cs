using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Resilience;

/// <summary>
/// Comprehensive tests for Resilience components to improve branch coverage.
/// </summary>
public class DefaultResiliencePipelineProviderTests
{
    [Fact]
    public void Constructor_WithDefaultOptions_ShouldSucceed()
    {
        var provider = new DefaultResiliencePipelineProvider();
        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomOptions_ShouldSucceed()
    {
        var options = new CatgaResilienceOptions
        {
            MediatorTimeout = TimeSpan.FromSeconds(5),
            TransportTimeout = TimeSpan.FromSeconds(10),
            PersistenceTimeout = TimeSpan.FromSeconds(3)
        };
        var provider = new DefaultResiliencePipelineProvider(options);
        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteMediatorAsync_Success_ShouldReturnResult()
    {
        var provider = new DefaultResiliencePipelineProvider();

        var result = await provider.ExecuteMediatorAsync(async ct =>
        {
            return "success";
        }, CancellationToken.None);

        result.Should().Be("success");
    }

    [Fact]
    public async Task ExecuteMediatorAsync_VoidAction_ShouldComplete()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecuteMediatorAsync(async ct =>
        {
            executed = true;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTransportPublishAsync_Success_ShouldReturnResult()
    {
        var provider = new DefaultResiliencePipelineProvider();

        var result = await provider.ExecuteTransportPublishAsync(async ct =>
        {
            return "published";
        }, CancellationToken.None);

        result.Should().Be("published");
    }

    [Fact]
    public async Task ExecuteTransportPublishAsync_VoidAction_ShouldComplete()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecuteTransportPublishAsync(async ct =>
        {
            executed = true;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTransportSendAsync_Success_ShouldReturnResult()
    {
        var provider = new DefaultResiliencePipelineProvider();

        var result = await provider.ExecuteTransportSendAsync(async ct =>
        {
            return "sent";
        }, CancellationToken.None);

        result.Should().Be("sent");
    }

    [Fact]
    public async Task ExecuteTransportSendAsync_VoidAction_ShouldComplete()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecuteTransportSendAsync(async ct =>
        {
            executed = true;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutePersistenceAsync_Success_ShouldReturnResult()
    {
        var provider = new DefaultResiliencePipelineProvider();

        var result = await provider.ExecutePersistenceAsync(async ct =>
        {
            return "persisted";
        }, CancellationToken.None);

        result.Should().Be("persisted");
    }

    [Fact]
    public async Task ExecutePersistenceAsync_VoidAction_ShouldComplete()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecutePersistenceAsync(async ct =>
        {
            executed = true;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutePersistenceNoRetryAsync_Success_ShouldReturnResult()
    {
        var provider = new DefaultResiliencePipelineProvider();

        var result = await provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            return "no-retry";
        }, CancellationToken.None);

        result.Should().Be("no-retry");
    }

    [Fact]
    public async Task ExecutePersistenceNoRetryAsync_VoidAction_ShouldComplete()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            executed = true;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTransportPublishAsync_WithRetry_ShouldRetryOnFailure()
    {
        var options = new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var provider = new DefaultResiliencePipelineProvider(options);

        var attempts = 0;
        var result = await provider.ExecuteTransportPublishAsync(async ct =>
        {
            attempts++;
            if (attempts < 3)
                throw new InvalidOperationException("Transient error");
            return "success";
        }, CancellationToken.None);

        result.Should().Be("success");
        attempts.Should().Be(3);
    }
}

/// <summary>
/// Tests for CatgaResilienceOptions.
/// </summary>
public class CatgaResilienceOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldBeReasonable()
    {
        var options = new CatgaResilienceOptions();

        options.MediatorTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        options.TransportTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        options.PersistenceTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        options.TransportRetryCount.Should().BeGreaterThan(0);
        options.PersistenceRetryCount.Should().BeGreaterThan(0);
        options.TransportRetryDelay.Should().BeGreaterThan(TimeSpan.Zero);
        options.PersistenceRetryDelay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void CustomValues_ShouldBeSet()
    {
        var options = new CatgaResilienceOptions
        {
            MediatorTimeout = TimeSpan.FromSeconds(10),
            MediatorBulkheadConcurrency = 100,
            MediatorBulkheadQueueLimit = 50,
            TransportTimeout = TimeSpan.FromSeconds(15),
            TransportRetryCount = 5,
            TransportRetryDelay = TimeSpan.FromSeconds(1),
            TransportBulkheadConcurrency = 200,
            TransportBulkheadQueueLimit = 100,
            PersistenceTimeout = TimeSpan.FromSeconds(5),
            PersistenceRetryCount = 4,
            PersistenceRetryDelay = TimeSpan.FromMilliseconds(500),
            PersistenceBulkheadConcurrency = 50,
            PersistenceBulkheadQueueLimit = 25
        };

        options.MediatorTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.MediatorBulkheadConcurrency.Should().Be(100);
        options.MediatorBulkheadQueueLimit.Should().Be(50);
        options.TransportTimeout.Should().Be(TimeSpan.FromSeconds(15));
        options.TransportRetryCount.Should().Be(5);
        options.TransportRetryDelay.Should().Be(TimeSpan.FromSeconds(1));
        options.TransportBulkheadConcurrency.Should().Be(200);
        options.TransportBulkheadQueueLimit.Should().Be(100);
        options.PersistenceTimeout.Should().Be(TimeSpan.FromSeconds(5));
        options.PersistenceRetryCount.Should().Be(4);
        options.PersistenceRetryDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        options.PersistenceBulkheadConcurrency.Should().Be(50);
        options.PersistenceBulkheadQueueLimit.Should().Be(25);
    }
}

/// <summary>
/// Tests for ResilienceKeys.
/// </summary>
public class ResilienceKeysTests
{
    [Fact]
    public void Mediator_ShouldHaveValue()
    {
        ResilienceKeys.Mediator.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TransportPublish_ShouldHaveValue()
    {
        ResilienceKeys.TransportPublish.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TransportSend_ShouldHaveValue()
    {
        ResilienceKeys.TransportSend.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Persistence_ShouldHaveValue()
    {
        ResilienceKeys.Persistence.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AllKeys_ShouldBeUnique()
    {
        var keys = new[]
        {
            ResilienceKeys.Mediator,
            ResilienceKeys.TransportPublish,
            ResilienceKeys.TransportSend,
            ResilienceKeys.Persistence
        };

        keys.Distinct().Should().HaveCount(keys.Length);
    }
}
