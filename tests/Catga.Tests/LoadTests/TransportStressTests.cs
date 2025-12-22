using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Catga.Transport.Nats;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using NATS.Client.Core;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.LoadTests;

/// <summary>
/// Transport stress tests to verify throughput requirements:
/// - InMemory: 100K+ messages/second
/// - Redis: 10K+ messages/second
/// - NATS: 10K+ messages/second
/// Requirements: 27.1-27.3
/// </summary>
public class TransportStressTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    
    // InMemory transport (always available)
    private InMemoryMessageTransport? _inMemoryTransport;
    
    // Redis transport (requires Docker)
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private RedisMessageTransport? _redisTransport;
    
    // NATS transport (requires Docker)
    private IContainer? _natsContainer;
    private INatsConnection? _natsConnection;
    private NatsMessageTransport? _natsTransport;

    public TransportStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Initialize InMemory transport (always available)
        _inMemoryTransport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        
        // Try to initialize Redis transport
        await InitializeRedisAsync();
        
        // Try to initialize NATS transport
        await InitializeNatsAsync();
    }

    private async Task InitializeRedisAsync()
    {
        if (!IsDockerRunning()) return;

        try
        {
            var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
            if (!IsImageAvailable(redisImage)) return;

            _redisContainer = new RedisBuilder()
                .WithImage(redisImage)
                .Build();

            await _redisContainer.StartAsync();

            var connectionString = _redisContainer.GetConnectionString();
            _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

            var serializer = new MemoryPackMessageSerializer();
            _redisTransport = new RedisMessageTransport(_redis, serializer, provider: new DiagnosticResiliencePipelineProvider());
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to initialize Redis: {ex.Message}");
        }
    }

    private async Task InitializeNatsAsync()
    {
        if (!IsDockerRunning()) return;

        try
        {
            var natsImage = Environment.GetEnvironmentVariable("TEST_NATS_IMAGE") ?? "nats:latest";
            if (!IsImageAvailable(natsImage)) return;

            _natsContainer = new ContainerBuilder()
                .WithImage(natsImage)
                .WithPortBinding(4222, true)
                .WithPortBinding(8222, true)
                .WithCommand("-js", "-m", "8222")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPort(8222)
                        .ForPath("/varz")))
                .Build();

            await _natsContainer.StartAsync();

            var port = _natsContainer.GetMappedPublicPort(4222);
            _natsConnection = new NatsConnection(new NatsOpts 
            { 
                Url = $"nats://localhost:{port}", 
                ConnectTimeout = TimeSpan.FromSeconds(10) 
            });
            await _natsConnection.ConnectAsync();

            _natsTransport = new NatsMessageTransport(
                _natsConnection, 
                new MemoryPackMessageSerializer(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<NatsMessageTransport>.Instance,
                options: null, 
                provider: new DiagnosticResiliencePipelineProvider());
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to initialize NATS: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        // Dispose NATS
        if (_natsTransport != null) await _natsTransport.DisposeAsync();
        if (_natsConnection != null) await _natsConnection.DisposeAsync();
        if (_natsContainer != null) await _natsContainer.DisposeAsync();

        // Dispose Redis
        if (_redisTransport != null) await _redisTransport.DisposeAsync();
        _redis?.Dispose();
        if (_redisContainer != null) await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
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

    private static bool IsImageAvailable(string image)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"image inspect {image}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }


    #region InMemory Transport Stress Tests

    /// <summary>
    /// Stress test: InMemory transport should handle 100K+ messages per second
    /// Requirements: 27.1
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public async Task Transport_InMemory_100KMessagesPerSecond()
    {
        // Arrange
        const int targetMessagesPerSecond = 100_000;
        const int testDurationSeconds = 5;
        const int totalMessages = targetMessagesPerSecond * testDurationSeconds;
        
        var receivedCount = 0L;
        var latencies = new ConcurrentBag<double>();
        
        // Subscribe to messages
        await _inMemoryTransport!.SubscribeAsync<StressTestMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            var latency = (DateTime.UtcNow - msg.SentAt).TotalMilliseconds;
            latencies.Add(latency);
            return Task.CompletedTask;
        });

        _output.WriteLine($"Starting InMemory stress test - Target: {targetMessagesPerSecond:N0} msg/sec for {testDurationSeconds}s...");

        var sw = Stopwatch.StartNew();
        var publishTasks = new List<Task>();
        var batchSize = 1000;
        var batches = totalMessages / batchSize;

        // Publish messages in batches for better throughput
        for (int batch = 0; batch < batches; batch++)
        {
            var messages = Enumerable.Range(0, batchSize)
                .Select(i => new StressTestMessage
                {
                    Id = batch * batchSize + i,
                    Data = $"stress-{batch}-{i}",
                    SentAt = DateTime.UtcNow
                })
                .ToList();

            publishTasks.Add(_inMemoryTransport.PublishBatchAsync(messages));

            // Yield occasionally to allow processing
            if (batch % 100 == 0)
            {
                await Task.Yield();
            }
        }

        await Task.WhenAll(publishTasks);
        
        // Wait for all messages to be processed
        var maxWait = TimeSpan.FromSeconds(30);
        var waitStart = Stopwatch.StartNew();
        while (Interlocked.Read(ref receivedCount) < totalMessages && waitStart.Elapsed < maxWait)
        {
            await Task.Delay(100);
        }

        sw.Stop();

        // Calculate statistics
        var actualReceived = Interlocked.Read(ref receivedCount);
        var actualTps = actualReceived / sw.Elapsed.TotalSeconds;
        var latencyList = latencies.ToList();
        latencyList.Sort();
        
        var p50 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.50)] : 0;
        var p95 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.95)] : 0;
        var p99 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.99)] : 0;

        _output.WriteLine($"=== InMemory Transport Stress Test Results ===");
        _output.WriteLine($"Target Messages: {totalMessages:N0}");
        _output.WriteLine($"Received Messages: {actualReceived:N0}");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Throughput: {actualTps:N0} msg/sec (Target: {targetMessagesPerSecond:N0})");
        _output.WriteLine($"Latency P50: {p50:F2}ms");
        _output.WriteLine($"Latency P95: {p95:F2}ms");
        _output.WriteLine($"Latency P99: {p99:F2}ms");
        _output.WriteLine($"Message Loss: {totalMessages - actualReceived:N0} ({(totalMessages - actualReceived) * 100.0 / totalMessages:F2}%)");

        // Assertions
        actualTps.Should().BeGreaterThan(targetMessagesPerSecond * 0.9, 
            $"InMemory transport should achieve at least 90% of target ({targetMessagesPerSecond:N0} msg/sec)");
        actualReceived.Should().BeGreaterOrEqualTo((long)(totalMessages * 0.99), 
            "should deliver at least 99% of messages");
    }

    #endregion

    #region Redis Transport Stress Tests

    /// <summary>
    /// Stress test: Redis transport should handle 10K+ messages per second
    /// Requirements: 27.2
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    [Trait("Category", "Integration")]
    [Trait("Requires", "Docker")]
    public async Task Transport_Redis_10KMessagesPerSecond()
    {
        if (_redisTransport == null)
        {
            _output.WriteLine("Skipping Redis stress test - Docker/Redis not available");
            return;
        }

        // Arrange
        const int targetMessagesPerSecond = 10_000;
        const int testDurationSeconds = 5;
        const int totalMessages = targetMessagesPerSecond * testDurationSeconds;
        
        var receivedCount = 0L;
        var latencies = new ConcurrentBag<double>();
        
        // Subscribe to messages
        await _redisTransport.SubscribeAsync<RedisStressMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            var latency = (DateTime.UtcNow - msg.SentAt).TotalMilliseconds;
            latencies.Add(latency);
            return Task.CompletedTask;
        });

        // Wait for subscription to be ready
        await Task.Delay(500);

        _output.WriteLine($"Starting Redis stress test - Target: {targetMessagesPerSecond:N0} msg/sec for {testDurationSeconds}s...");

        var sw = Stopwatch.StartNew();
        var publishTasks = new List<Task>();
        var batchSize = 100; // Smaller batches for Redis
        var batches = totalMessages / batchSize;

        // Publish messages in batches
        for (int batch = 0; batch < batches; batch++)
        {
            var messages = Enumerable.Range(0, batchSize)
                .Select(i => new RedisStressMessage
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    Id = batch * batchSize + i,
                    Data = $"redis-stress-{batch}-{i}",
                    SentAt = DateTime.UtcNow
                })
                .ToList();

            publishTasks.Add(_redisTransport.PublishBatchAsync(messages));

            // Rate limiting to avoid overwhelming Redis
            if (batch % 50 == 0)
            {
                await Task.Delay(1);
            }
        }

        await Task.WhenAll(publishTasks);
        
        // Wait for messages to be processed
        var maxWait = TimeSpan.FromSeconds(60);
        var waitStart = Stopwatch.StartNew();
        while (Interlocked.Read(ref receivedCount) < totalMessages * 0.95 && waitStart.Elapsed < maxWait)
        {
            await Task.Delay(100);
            if (waitStart.ElapsedMilliseconds % 5000 < 100)
            {
                _output.WriteLine($"Progress: {Interlocked.Read(ref receivedCount):N0}/{totalMessages:N0} received");
            }
        }

        sw.Stop();

        // Calculate statistics
        var actualReceived = Interlocked.Read(ref receivedCount);
        var actualTps = actualReceived / sw.Elapsed.TotalSeconds;
        var latencyList = latencies.ToList();
        latencyList.Sort();
        
        var p50 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.50)] : 0;
        var p95 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.95)] : 0;
        var p99 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.99)] : 0;

        _output.WriteLine($"=== Redis Transport Stress Test Results ===");
        _output.WriteLine($"Target Messages: {totalMessages:N0}");
        _output.WriteLine($"Received Messages: {actualReceived:N0}");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Throughput: {actualTps:N0} msg/sec (Target: {targetMessagesPerSecond:N0})");
        _output.WriteLine($"Latency P50: {p50:F2}ms");
        _output.WriteLine($"Latency P95: {p95:F2}ms");
        _output.WriteLine($"Latency P99: {p99:F2}ms");
        _output.WriteLine($"Message Loss: {totalMessages - actualReceived:N0} ({(totalMessages - actualReceived) * 100.0 / totalMessages:F2}%)");

        // Assertions - Redis should achieve at least 80% of target due to network overhead
        actualTps.Should().BeGreaterThan(targetMessagesPerSecond * 0.8, 
            $"Redis transport should achieve at least 80% of target ({targetMessagesPerSecond:N0} msg/sec)");
        actualReceived.Should().BeGreaterOrEqualTo((long)(totalMessages * 0.95), 
            "should deliver at least 95% of messages");
    }

    #endregion


    #region NATS Transport Stress Tests

    /// <summary>
    /// Stress test: NATS transport should handle 10K+ messages per second
    /// Requirements: 27.3
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    [Trait("Category", "Integration")]
    [Trait("Requires", "Docker")]
    public async Task Transport_NATS_10KMessagesPerSecond()
    {
        if (_natsTransport == null)
        {
            _output.WriteLine("Skipping NATS stress test - Docker/NATS not available");
            return;
        }

        // Arrange
        const int targetMessagesPerSecond = 10_000;
        const int testDurationSeconds = 5;
        const int totalMessages = targetMessagesPerSecond * testDurationSeconds;
        
        var receivedCount = 0L;
        var latencies = new ConcurrentBag<double>();
        
        // Subscribe to messages
        await _natsTransport.SubscribeAsync<NatsStressMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            var latency = (DateTime.UtcNow - msg.SentAt).TotalMilliseconds;
            latencies.Add(latency);
            return Task.CompletedTask;
        });

        // Wait for subscription to be ready
        await Task.Delay(500);

        _output.WriteLine($"Starting NATS stress test - Target: {targetMessagesPerSecond:N0} msg/sec for {testDurationSeconds}s...");

        var sw = Stopwatch.StartNew();
        var publishTasks = new List<Task>();
        var batchSize = 100; // Smaller batches for NATS
        var batches = totalMessages / batchSize;

        // Publish messages in batches
        for (int batch = 0; batch < batches; batch++)
        {
            var messages = Enumerable.Range(0, batchSize)
                .Select(i => new NatsStressMessage
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    Id = batch * batchSize + i,
                    Data = $"nats-stress-{batch}-{i}",
                    SentAt = DateTime.UtcNow
                })
                .ToList();

            publishTasks.Add(_natsTransport.PublishBatchAsync(messages));

            // Rate limiting to avoid overwhelming NATS
            if (batch % 50 == 0)
            {
                await Task.Delay(1);
            }
        }

        await Task.WhenAll(publishTasks);
        
        // Wait for messages to be processed
        var maxWait = TimeSpan.FromSeconds(60);
        var waitStart = Stopwatch.StartNew();
        while (Interlocked.Read(ref receivedCount) < totalMessages * 0.95 && waitStart.Elapsed < maxWait)
        {
            await Task.Delay(100);
            if (waitStart.ElapsedMilliseconds % 5000 < 100)
            {
                _output.WriteLine($"Progress: {Interlocked.Read(ref receivedCount):N0}/{totalMessages:N0} received");
            }
        }

        sw.Stop();

        // Calculate statistics
        var actualReceived = Interlocked.Read(ref receivedCount);
        var actualTps = actualReceived / sw.Elapsed.TotalSeconds;
        var latencyList = latencies.ToList();
        latencyList.Sort();
        
        var p50 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.50)] : 0;
        var p95 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.95)] : 0;
        var p99 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.99)] : 0;

        _output.WriteLine($"=== NATS Transport Stress Test Results ===");
        _output.WriteLine($"Target Messages: {totalMessages:N0}");
        _output.WriteLine($"Received Messages: {actualReceived:N0}");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Throughput: {actualTps:N0} msg/sec (Target: {targetMessagesPerSecond:N0})");
        _output.WriteLine($"Latency P50: {p50:F2}ms");
        _output.WriteLine($"Latency P95: {p95:F2}ms");
        _output.WriteLine($"Latency P99: {p99:F2}ms");
        _output.WriteLine($"Message Loss: {totalMessages - actualReceived:N0} ({(totalMessages - actualReceived) * 100.0 / totalMessages:F2}%)");

        // Assertions - NATS should achieve at least 80% of target due to network overhead
        actualTps.Should().BeGreaterThan(targetMessagesPerSecond * 0.8, 
            $"NATS transport should achieve at least 80% of target ({targetMessagesPerSecond:N0} msg/sec)");
        actualReceived.Should().BeGreaterOrEqualTo((long)(totalMessages * 0.95), 
            "should deliver at least 95% of messages");
    }

    #endregion

    #region Sustained Load Tests

    /// <summary>
    /// Test sustained load over extended period for InMemory transport
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public async Task Transport_InMemory_SustainedLoad()
    {
        // Arrange
        const int targetTps = 50_000;
        const int durationSeconds = 10;
        const int intervalMs = 100;
        const int messagesPerInterval = targetTps * intervalMs / 1000;

        var receivedCount = 0L;
        var latencies = new ConcurrentBag<double>();

        await _inMemoryTransport!.SubscribeAsync<StressTestMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            latencies.Add((DateTime.UtcNow - msg.SentAt).TotalMilliseconds);
            return Task.CompletedTask;
        });

        _output.WriteLine($"Starting InMemory sustained load test - {targetTps:N0} TPS for {durationSeconds}s...");

        var sw = Stopwatch.StartNew();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var totalSent = 0L;

        while (!cts.Token.IsCancellationRequested)
        {
            var messages = Enumerable.Range(0, messagesPerInterval)
                .Select(i => new StressTestMessage
                {
                    Id = (int)Interlocked.Increment(ref totalSent),
                    Data = $"sustained-{totalSent}",
                    SentAt = DateTime.UtcNow
                })
                .ToList();

            await _inMemoryTransport.PublishBatchAsync(messages);
            await Task.Delay(intervalMs);

            if (sw.ElapsedMilliseconds % 2000 < intervalMs)
            {
                var currentTps = Interlocked.Read(ref receivedCount) / Math.Max(1, sw.Elapsed.TotalSeconds);
                _output.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F0}s, Received: {receivedCount:N0}, TPS: {currentTps:N0}");
            }
        }

        // Wait for remaining messages
        await Task.Delay(1000);
        sw.Stop();

        var actualReceived = Interlocked.Read(ref receivedCount);
        var actualTps = actualReceived / sw.Elapsed.TotalSeconds;
        var latencyList = latencies.ToList();
        latencyList.Sort();

        var p50 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.50)] : 0;
        var p95 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.95)] : 0;
        var p99 = latencyList.Any() ? latencyList[(int)(latencyList.Count * 0.99)] : 0;

        _output.WriteLine($"=== InMemory Sustained Load Test Results ===");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"Total Sent: {totalSent:N0}");
        _output.WriteLine($"Total Received: {actualReceived:N0}");
        _output.WriteLine($"Actual TPS: {actualTps:N0} (Target: {targetTps:N0})");
        _output.WriteLine($"Latency P50: {p50:F2}ms, P95: {p95:F2}ms, P99: {p99:F2}ms");

        actualTps.Should().BeGreaterThan(targetTps * 0.9, "should maintain at least 90% of target TPS");
    }

    #endregion

    #region Concurrent Publisher Tests

    /// <summary>
    /// Test multiple concurrent publishers for InMemory transport
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public async Task Transport_InMemory_ConcurrentPublishers()
    {
        // Arrange
        const int publisherCount = 10;
        const int messagesPerPublisher = 10_000;
        const int totalMessages = publisherCount * messagesPerPublisher;

        var receivedCount = 0L;
        var receivedIds = new ConcurrentBag<int>();

        await _inMemoryTransport!.SubscribeAsync<StressTestMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            receivedIds.Add(msg.Id);
            return Task.CompletedTask;
        });

        _output.WriteLine($"Starting concurrent publishers test - {publisherCount} publishers, {messagesPerPublisher:N0} messages each...");

        var sw = Stopwatch.StartNew();

        // Start multiple publishers concurrently
        var publisherTasks = Enumerable.Range(0, publisherCount).Select(publisherId => Task.Run(async () =>
        {
            for (int i = 0; i < messagesPerPublisher; i++)
            {
                var msg = new StressTestMessage
                {
                    Id = publisherId * messagesPerPublisher + i,
                    Data = $"publisher-{publisherId}-msg-{i}",
                    SentAt = DateTime.UtcNow
                };
                await _inMemoryTransport.PublishAsync(msg);
            }
        })).ToArray();

        await Task.WhenAll(publisherTasks);

        // Wait for all messages to be processed
        var maxWait = TimeSpan.FromSeconds(30);
        var waitStart = Stopwatch.StartNew();
        while (Interlocked.Read(ref receivedCount) < totalMessages && waitStart.Elapsed < maxWait)
        {
            await Task.Delay(100);
        }

        sw.Stop();

        var actualReceived = Interlocked.Read(ref receivedCount);
        var throughput = actualReceived / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"=== Concurrent Publishers Test Results ===");
        _output.WriteLine($"Publishers: {publisherCount}");
        _output.WriteLine($"Messages per Publisher: {messagesPerPublisher:N0}");
        _output.WriteLine($"Total Expected: {totalMessages:N0}");
        _output.WriteLine($"Total Received: {actualReceived:N0}");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Throughput: {throughput:N0} msg/sec");

        // Verify no message loss
        actualReceived.Should().Be(totalMessages, "all messages should be delivered");
        
        // Verify no duplicates
        var uniqueIds = receivedIds.Distinct().Count();
        uniqueIds.Should().Be(totalMessages, "no duplicate messages should be received");
    }

    #endregion
}

#region Test Message Types

[MemoryPackable]
public partial record StressTestMessage
{
    public int Id { get; init; }
    public string Data { get; init; } = string.Empty;
    public DateTime SentAt { get; init; }
}

[MemoryPackable]
public partial record RedisStressMessage : IMessage
{
    public required long MessageId { get; init; }
    public int Id { get; init; }
    public string Data { get; init; } = string.Empty;
    public DateTime SentAt { get; init; }
    public QualityOfService QoS => QualityOfService.AtMostOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

[MemoryPackable]
public partial record NatsStressMessage : IMessage
{
    public required long MessageId { get; init; }
    public int Id { get; init; }
    public string Data { get; init; } = string.Empty;
    public DateTime SentAt { get; init; }
    public QualityOfService QoS => QualityOfService.AtMostOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

#endregion
