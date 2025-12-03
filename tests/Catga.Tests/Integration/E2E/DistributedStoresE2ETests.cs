using Catga.Abstractions;
using Catga.Core;
using Catga.Persistence.Nats;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E tests for distributed stores after refactoring.
/// Tests Redis and NATS implementations with real containers.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class DistributedStoresE2ETests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _nats;
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        // NATS with JetStream
        var natsImage = ResolveImage("TEST_NATS_IMAGE", "nats:latest");
        if (natsImage is not null)
        {
            _natsContainer = new ContainerBuilder()
                .WithImage(natsImage)
                .WithPortBinding(4222, true)
                .WithPortBinding(8222, true)
                .WithCommand("-js", "-m", "8222")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/varz")))
                .Build();
            await _natsContainer.StartAsync();
            var natsPort = _natsContainer.GetMappedPublicPort(4222);
            _nats = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{natsPort}", ConnectTimeout = TimeSpan.FromSeconds(10) });
            await _nats.ConnectAsync();
        }

        // Redis
        var redisImage = ResolveImage("TEST_REDIS_IMAGE", "redis:7-alpine");
        if (redisImage is not null)
        {
            _redisContainer = new RedisBuilder().WithImage(redisImage).Build();
            await _redisContainer.StartAsync();
            _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        }
    }

    public async Task DisposeAsync()
    {
        if (_nats is not null) await _nats.DisposeAsync();
        if (_natsContainer is not null) await _natsContainer.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_redisContainer is not null) await _redisContainer.DisposeAsync();
    }

    #region Redis Idempotency Store E2E Tests

    [Fact]
    public async Task Redis_IdempotencyStore_MarkAndCheck_ShouldWork()
    {
        if (_redis is null) return;

        // Arrange
        var store = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        // Act
        var beforeMark = await store.HasBeenProcessedAsync(messageId);
        await store.MarkAsProcessedAsync(messageId, new E2ETestResult { Value = 42 });
        var afterMark = await store.HasBeenProcessedAsync(messageId);
        var cached = await store.GetCachedResultAsync<E2ETestResult>(messageId);

        // Assert
        beforeMark.Should().BeFalse();
        afterMark.Should().BeTrue();
        cached.Should().NotBeNull();
        cached!.Value.Should().Be(42);
    }

    [Fact]
    public async Task Redis_IdempotencyStore_ConcurrentMarks_ShouldBeIdempotent()
    {
        if (_redis is null) return;

        // Arrange
        var store = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        // Act - concurrent marks
        var tasks = Enumerable.Range(0, 10).Select(i =>
            store.MarkAsProcessedAsync(messageId, new E2ETestResult { Value = i }));
        await Task.WhenAll(tasks);

        var isProcessed = await store.HasBeenProcessedAsync(messageId);

        // Assert
        isProcessed.Should().BeTrue();
    }

    #endregion

    #region Redis Snapshot Store E2E Tests

    [Fact]
    public async Task Redis_SnapshotStore_SaveAndLoad_ShouldWork()
    {
        if (_redis is null) return;

        // Arrange
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new Catga.EventSourcing.SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var aggregate = new E2ETestAggregate { Id = "e2e-agg-1", Counter = 42 };

        // Act
        await store.SaveAsync("e2e-stream-1", aggregate, version: 10);
        var snapshot = await store.LoadAsync<E2ETestAggregate>("e2e-stream-1");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(10);
        snapshot.Value.State.Counter.Should().Be(42);
    }

    [Fact]
    public async Task Redis_SnapshotStore_Delete_ShouldRemoveSnapshot()
    {
        if (_redis is null) return;

        // Arrange
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new Catga.EventSourcing.SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var aggregate = new E2ETestAggregate { Id = "e2e-agg-del", Counter = 99 };
        await store.SaveAsync("e2e-stream-del", aggregate, version: 5);

        // Act
        await store.DeleteAsync("e2e-stream-del");
        var snapshot = await store.LoadAsync<E2ETestAggregate>("e2e-stream-del");

        // Assert
        snapshot.Should().BeNull();
    }

    #endregion

    #region NATS Idempotency Store E2E Tests

    [Fact]
    public async Task NATS_IdempotencyStore_MarkAndCheck_ShouldWork()
    {
        if (_nats is null) return;

        // Arrange
        var streamName = $"IDEM_E2E_{Guid.NewGuid():N}";
        var store = new NatsJSIdempotencyStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        // Act
        var beforeMark = await store.HasBeenProcessedAsync(messageId);
        await store.MarkAsProcessedAsync(messageId, new E2ETestResult { Value = 99 });
        await Task.Delay(200); // Allow JetStream to persist
        var afterMark = await store.HasBeenProcessedAsync(messageId);

        // Assert
        beforeMark.Should().BeFalse();
        afterMark.Should().BeTrue();
    }

    #endregion

    #region Resilience Pipeline E2E Tests

    [Fact]
    public async Task ResiliencePipeline_ExecutePersistence_ShouldSucceed()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider(new CatgaResilienceOptions
        {
            PersistenceRetryCount = 3,
            PersistenceRetryDelay = TimeSpan.FromMilliseconds(10)
        });
        var counter = 0;

        // Act
        var result = await provider.ExecutePersistenceAsync(async ct =>
        {
            counter++;
            await Task.Delay(1, ct);
            return counter;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        counter.Should().Be(1);
    }

    [Fact]
    public async Task ResiliencePipeline_ExecuteTransport_ShouldSucceed()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider(new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(10)
        });
        var counter = 0;

        // Act
        var result = await provider.ExecuteTransportPublishAsync(async ct =>
        {
            counter++;
            await Task.Delay(1, ct);
            return counter;
        }, CancellationToken.None);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task ResiliencePipeline_WithRetry_ShouldRetryOnFailure()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider(new CatgaResilienceOptions
        {
            PersistenceRetryCount = 3,
            PersistenceRetryDelay = TimeSpan.FromMilliseconds(10)
        });
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

    #endregion

    #region Helpers

    private static bool IsDockerRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? ResolveImage(string envVar, string defaultImage)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrEmpty(env) ? defaultImage : env;
    }

    #endregion
}

[MemoryPackable]
public partial class E2ETestResult
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class E2ETestAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Counter { get; set; }
}
