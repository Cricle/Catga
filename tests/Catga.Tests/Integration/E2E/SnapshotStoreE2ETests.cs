using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Redis.Stores;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed partial class SnapshotStoreE2ETests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        if (_redisContainer is not null)
            await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Redis_SnapshotStore_SaveAndLoad()
    {
        if (_redis is null) return;

        var options = Options.Create(new SnapshotOptions());
        var logger = NullLogger<RedisSnapshotStore>.Instance;
        var store = new RedisSnapshotStore(_redis, _serializer, options, logger);

        var streamId = $"order-{Guid.NewGuid()}";
        var aggregate = new TestOrderAggregate
        {
            Id = "order-123",
            CustomerId = "customer-456",
            TotalAmount = 99.99m,
            Status = "Confirmed"
        };

        // Save snapshot
        await store.SaveAsync(streamId, aggregate, version: 10);

        // Load snapshot
        var snapshot = await store.LoadAsync<TestOrderAggregate>(streamId);

        snapshot.Should().NotBeNull();
        snapshot!.Value.StreamId.Should().Be(streamId);
        snapshot.Value.Version.Should().Be(10);
        snapshot.Value.State.Id.Should().Be("order-123");
        snapshot.Value.State.CustomerId.Should().Be("customer-456");
        snapshot.Value.State.TotalAmount.Should().Be(99.99m);
        snapshot.Value.State.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task Redis_SnapshotStore_Delete()
    {
        if (_redis is null) return;

        var options = Options.Create(new SnapshotOptions());
        var logger = NullLogger<RedisSnapshotStore>.Instance;
        var store = new RedisSnapshotStore(_redis, _serializer, options, logger);

        var streamId = $"order-{Guid.NewGuid()}";
        var aggregate = new TestOrderAggregate { Id = "del-1", CustomerId = "c1", TotalAmount = 10m, Status = "New" };

        await store.SaveAsync(streamId, aggregate, version: 5);

        // Verify exists
        var snapshot = await store.LoadAsync<TestOrderAggregate>(streamId);
        snapshot.Should().NotBeNull();

        // Delete
        await store.DeleteAsync(streamId);

        // Verify deleted
        var snapshotAfter = await store.LoadAsync<TestOrderAggregate>(streamId);
        snapshotAfter.Should().BeNull();
    }

    [Fact]
    public async Task Redis_SnapshotStore_UpdateSnapshot()
    {
        if (_redis is null) return;

        var options = Options.Create(new SnapshotOptions());
        var logger = NullLogger<RedisSnapshotStore>.Instance;
        var store = new RedisSnapshotStore(_redis, _serializer, options, logger);

        var streamId = $"order-{Guid.NewGuid()}";

        // Save initial snapshot
        var aggregate1 = new TestOrderAggregate { Id = "upd-1", CustomerId = "c1", TotalAmount = 10m, Status = "New" };
        await store.SaveAsync(streamId, aggregate1, version: 5);

        // Update snapshot
        var aggregate2 = new TestOrderAggregate { Id = "upd-1", CustomerId = "c1", TotalAmount = 50m, Status = "Shipped" };
        await store.SaveAsync(streamId, aggregate2, version: 15);

        // Load and verify
        var snapshot = await store.LoadAsync<TestOrderAggregate>(streamId);
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(15);
        snapshot.Value.State.TotalAmount.Should().Be(50m);
        snapshot.Value.State.Status.Should().Be("Shipped");
    }

    [MemoryPackable]
    private partial class TestOrderAggregate
    {
        public string Id { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
