using Catga.Persistence.Nats;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Integration.Nats;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Collection("IntegrationTests")]
public sealed class NatsSubscriptionLockE2ETests
{
    private readonly SharedIntegrationFixture _fixture;

    public NatsSubscriptionLockE2ETests(SharedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SubscriptionLock_WrongOwnerCannotRelease_AndOriginalOwnerStillBlocksOthers()
    {
        if (_fixture.NatsConnection is null) return;

        var bucketName = $"SUBLOCK_{Guid.NewGuid():N}";
        var store = new NatsSubscriptionStore(
            _fixture.NatsConnection,
            new MemoryPackMessageSerializer(),
            new DiagnosticResiliencePipelineProvider(),
            bucketName);
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
        if (_fixture.NatsConnection is null) return;

        var bucketName = $"SUBLOCK_{Guid.NewGuid():N}";
        var store = new NatsSubscriptionStore(
            _fixture.NatsConnection,
            new MemoryPackMessageSerializer(),
            new DiagnosticResiliencePipelineProvider(),
            bucketName);
        var subscriptionName = $"sub-{Guid.NewGuid():N}";

        var acquireTasks = Enumerable.Range(1, 12)
            .Select(i => store.TryAcquireLockAsync(subscriptionName, $"consumer-{i}").AsTask());

        var results = await Task.WhenAll(acquireTasks);

        results.Count(static acquired => acquired).Should().Be(1);
    }

    [Fact]
    public async Task SubscriptionLock_ExpiredLock_AllowsReacquire()
    {
        if (_fixture.NatsConnection is null) return;

        var bucketName = $"SUBLOCK_{Guid.NewGuid():N}";
        var store = new NatsSubscriptionStore(
            _fixture.NatsConnection,
            new MemoryPackMessageSerializer(),
            new DiagnosticResiliencePipelineProvider(),
            bucketName,
            TimeSpan.FromMilliseconds(150));
        var subscriptionName = $"sub-{Guid.NewGuid():N}";

        var firstAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-1");
        firstAcquire.Should().BeTrue();

        await Task.Delay(250);

        var secondAcquire = await store.TryAcquireLockAsync(subscriptionName, "consumer-2");
        secondAcquire.Should().BeTrue("expired lock should allow another consumer to acquire");
    }
}
