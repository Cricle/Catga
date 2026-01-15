using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Redis;
using Testcontainers.Nats;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Shared test infrastructure that manages Redis and NATS containers using Testcontainers.
/// Uses Alpine versions for faster startup and smaller footprint.
/// Containers are started once and shared across all tests.
/// Each test class uses a unique key prefix for data isolation.
/// </summary>
public class SharedTestInfrastructure : IAsyncLifetime
{
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static SharedTestInfrastructure? _instance;
    private static bool _isInitialized = false;

    private RedisContainer? _redisContainer;
    private NatsContainer? _natsContainer;

    public string RedisConnectionString { get; private set; } = string.Empty;
    public string NatsConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Get the singleton instance of shared test infrastructure
    /// </summary>
    public static SharedTestInfrastructure GetInstance()
    {
        if (_instance == null)
        {
            _instance = new SharedTestInfrastructure();
        }
        return _instance;
    }

    public async Task InitializeAsync()
    {
        // Use semaphore to ensure only one initialization happens
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized)
                return;

            Console.WriteLine("🚀 Starting shared test infrastructure...");

            // Start Redis container (Alpine version)
            await StartRedisContainerAsync();

            // Start NATS container (Alpine version)
            await StartNatsContainerAsync();

            _isInitialized = true;
            Console.WriteLine("✓ Test infrastructure ready");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to initialize test infrastructure: {ex.Message}");
            Console.WriteLine("Tests will fall back to InMemory implementations");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task StartRedisContainerAsync()
    {
        try
        {
            _redisContainer = new RedisBuilder()
                .WithImage("redis:7-alpine")
                .WithName($"catga-test-redis-{Guid.NewGuid():N}")
                .WithCommand("redis-server", "--save", "", "--appendonly", "no")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
                .WithCleanUp(true)
                .Build();

            await _redisContainer.StartAsync();
            
            RedisConnectionString = _redisContainer.GetConnectionString();
            Console.WriteLine($"✓ Redis container started: {RedisConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Redis container failed to start: {ex.Message}");
        }
    }

    private async Task StartNatsContainerAsync()
    {
        try
        {
            _natsContainer = new NatsBuilder()
                .WithImage("nats:2-alpine")
                .WithName($"catga-test-nats-{Guid.NewGuid():N}")
                .WithCleanUp(true)
                .Build();

            await _natsContainer.StartAsync();
            
            NatsConnectionString = _natsContainer.GetConnectionString();
            Console.WriteLine($"✓ NATS container started: {NatsConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ NATS container failed to start: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (!_isInitialized)
                return;

            Console.WriteLine("🛑 Stopping shared test infrastructure...");

            if (_redisContainer != null)
            {
                await _redisContainer.StopAsync();
                await _redisContainer.DisposeAsync();
                Console.WriteLine("✓ Redis container stopped");
            }

            if (_natsContainer != null)
            {
                await _natsContainer.StopAsync();
                await _natsContainer.DisposeAsync();
                Console.WriteLine("✓ NATS container stopped");
            }

            _isInitialized = false;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Generate a unique key prefix for test isolation
    /// </summary>
    public static string GenerateKeyPrefix(string testClassName)
    {
        return $"test:{testClassName}:{Guid.NewGuid():N}:";
    }
}

/// <summary>
/// Collection fixture that provides shared test infrastructure
/// </summary>
[CollectionDefinition("SharedInfrastructure")]
public class SharedInfrastructureCollection : ICollectionFixture<SharedTestInfrastructure>
{
}
