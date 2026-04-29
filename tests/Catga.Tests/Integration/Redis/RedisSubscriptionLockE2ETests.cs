using Catga.EventSourcing;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Integration.Redis;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Collection("IntegrationTests")]
public sealed class RedisSubscriptionLockE2ETests
{
    private readonly SharedIntegrationFixture _fixture;

    public RedisSubscriptionLockE2ETests(SharedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SubscriptionLock_WrongOwnerCannotRelease_AndOriginalOwnerStillBlocksOthers()
    {
        if (_fixture.Redis is null) return;

        var prefix = $"test:subscription:{Guid.NewGuid():N}:";
        var store = new RedisSubscriptionStore(_fixture.Redis, new DiagnosticResiliencePipelineProvider(), prefix);
        var subscriptionName = $"sub-{Guid.NewGuid():N}";

        var firstAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-1");
        firstAcquire.Should().BeTrue();

        await store.ReleaseLockAsync(subscriptionName, "consumer-2");

        var secondAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-3");
        secondAcquire.Should().BeFalse();

        await store.ReleaseLockAsync(subscriptionName, "consumer-1");

        var thirdAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-3");
        thirdAcquire.Should().BeTrue();
    }

    [Fact]
    public async Task SubscriptionLock_ConcurrentAcquire_OnlyOneConsumerSucceeds()
    {
        if (_fixture.Redis is null) return;

        var prefix = $"test:subscription:{Guid.NewGuid():N}:";
        var store = new RedisSubscriptionStore(_fixture.Redis, new DiagnosticResiliencePipelineProvider(), prefix);
        var subscriptionName = $"sub-{Guid.NewGuid():N}";

        var acquireTasks = Enumerable.Range(1, 12)
            .Select(i => store.TryAcquireLockAsync(subscriptionName, $"consumer-{i}").AsTask());

        var results = await Task.WhenAll(acquireTasks);

        results.Count(static acquired => acquired).Should().Be(1);
    }

    [Fact]
    public async Task SubscriptionLock_ExpiredLock_AllowsReacquire()
    {
        if (_fixture.Redis is null) return;

        var prefix = $"test:subscription:{Guid.NewGuid():N}:";
        var store = new RedisSubscriptionStore(_fixture.Redis, new DiagnosticResiliencePipelineProvider(), prefix, TimeSpan.FromMilliseconds(150));
        var subscriptionName = $"sub-{Guid.NewGuid():N}";

        var firstAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-1");
        firstAcquire.Should().BeTrue();

        await Task.Delay(250);

        var secondAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-2");
        secondAcquire.Should().BeTrue("expired lock should allow another consumer to acquire");
    }
}
