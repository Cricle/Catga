using Catga.Flow.Dsl;
using Catga.Persistence.Nats.Flow;
using Catga.Persistence.Redis.Flow;
using Catga.Tests.Integration;

namespace Catga.Tests.Helpers;

public static class DistributedDslFlowStoreFactory
{
    public static async Task<IDslFlowStore> CreateAsync(string storeType)
    {
        return storeType switch
        {
            "InMemory" => TestStoreExtensions.CreateTestFlowStore(),
            "Redis" => await CreateRedisStoreAsync(),
            "Nats" => await CreateNatsStoreAsync(),
            _ => throw new ArgumentException($"Unknown store type: {storeType}")
        };
    }

    private static async Task<IDslFlowStore> CreateRedisStoreAsync()
    {
        var fixture = SharedIntegrationFixture.Instance;
        await fixture.InitializeAsync();

        Skip.IfNot(
            fixture.IsDockerAvailable && fixture.Redis is not null,
            "Redis integration infrastructure is not available");

        return new RedisDslFlowStore(
            fixture.Redis!,
            new TestMessageSerializer(),
            $"tdd-dslflow-{Guid.NewGuid():N}:");
    }

    private static async Task<IDslFlowStore> CreateNatsStoreAsync()
    {
        var fixture = SharedIntegrationFixture.Instance;
        await fixture.InitializeAsync();

        Skip.IfNot(
            fixture.IsDockerAvailable && fixture.NatsConnection is not null,
            "NATS integration infrastructure is not available");

        return new NatsDslFlowStore(
            fixture.NatsConnection!,
            new TestMessageSerializer(),
            $"tdd_dslflows_{Guid.NewGuid():N}");
    }
}
