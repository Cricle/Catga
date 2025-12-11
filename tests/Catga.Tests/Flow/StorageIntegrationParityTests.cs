using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Catga.Persistence.Redis.Flow;
using Catga.Persistence.Nats.Flow;
using Catga.Abstractions;
using System.Buffers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using StackExchange.Redis;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Tests.Flow;

/// <summary>
/// Integration tests that verify parity with real Redis and NATS connections.
/// These tests are skipped if the services are not available.
/// </summary>
public class StorageIntegrationParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IConnectionMultiplexer? _redisConnection;
    private INatsConnection? _natsConnection;
    private INatsJSContext? _jetStream;
    private readonly List<IDslFlowStore> _stores = new();
    private readonly bool _redisAvailable;
    private readonly bool _natsAvailable;

    public StorageIntegrationParityTests(ITestOutputHelper output)
    {
        _output = output;
        _redisAvailable = CheckRedisAvailable();
        _natsAvailable = CheckNatsAvailable();
    }

    public async Task InitializeAsync()
    {
        // Always add InMemory store
        _stores.Add(new InMemoryDslFlowStore());

        // Try to connect to Redis
        if (_redisAvailable)
        {
            try
            {
                _redisConnection = await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false,connectTimeout=1000");
                var serializer = new IntegrationTestSerializer();
                _stores.Add(new RedisDslFlowStore(_redisConnection, serializer, "integration:"));
                _output.WriteLine("✓ Redis connection established");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ Redis connection failed: {ex.Message}");
            }
        }

        // Try to connect to NATS
        if (_natsAvailable)
        {
            try
            {
                var opts = new NatsOpts { Url = "nats://localhost:4222", ConnectTimeout = TimeSpan.FromSeconds(1) };
                _natsConnection = new NatsConnection(opts);
                await _natsConnection.ConnectAsync();
                _jetStream = new NatsJSContext(_natsConnection);

                // Create test buckets
                await CreateNatsBucketsAsync();

                var serializer = new IntegrationTestSerializer();
                _stores.Add(new NatsDslFlowStore(_natsConnection, serializer, "integration"));
                _output.WriteLine("✓ NATS connection established");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️ NATS connection failed: {ex.Message}");
            }
        }

        _output.WriteLine($"Testing with {_stores.Count} store(s)");
    }

    public async Task DisposeAsync()
    {
        _redisConnection?.Dispose();

        if (_natsConnection != null)
        {
            await _natsConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task RealStores_CRUD_Operations_AreIdentical()
    {
        if (_stores.Count == 1)
        {
            _output.WriteLine("⚠️ Skipping - Only InMemory store available");
            return;
        }

        var flowId = $"integration-crud-{Guid.NewGuid()}";
        var results = new Dictionary<string, List<string>>();

        foreach (var store in _stores)
        {
            var storeName = store.GetType().Name;
            results[storeName] = new List<string>();

            try
            {
                // CREATE
                var state = new IntegrationTestState
                {
                    FlowId = flowId,
                    Data = $"Test data for {storeName}",
                    Counter = 42,
                    Items = new List<string> { "item1", "item2", "item3" }
                };

                var snapshot = new FlowSnapshot<IntegrationTestState>
                {
                    FlowId = flowId,
                    State = state,
                    Status = DslFlowStatus.Running,
                    Position = new FlowPosition(new[] { 1, 2, 3 }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Version = 1
                };

                var createResult = await store.CreateAsync(snapshot);
                results[storeName].Add($"Create: {createResult.IsSuccess}");

                // GET
                var retrieved = await store.GetAsync<IntegrationTestState>(flowId);
                results[storeName].Add($"Get: {retrieved != null}");
                if (retrieved != null)
                {
                    results[storeName].Add($"Data: {retrieved.State.Data}");
                    results[storeName].Add($"Counter: {retrieved.State.Counter}");
                    results[storeName].Add($"Items: {retrieved.State.Items.Count}");
                    results[storeName].Add($"Position: {string.Join(",", retrieved.Position.Path)}");
                }

                // UPDATE
                retrieved!.State.Counter = 100;
                retrieved.Status = DslFlowStatus.Completed;
                var updateResult = await store.UpdateAsync(retrieved);
                results[storeName].Add($"Update: {updateResult.IsSuccess}");

                // VERIFY UPDATE
                var updated = await store.GetAsync<IntegrationTestState>(flowId);
                results[storeName].Add($"Updated Counter: {updated?.State.Counter}");
                results[storeName].Add($"Updated Status: {updated?.Status}");
                results[storeName].Add($"Updated Version: {updated?.Version}");

                // DELETE
                var deleteResult = await store.DeleteAsync(flowId);
                results[storeName].Add($"Delete: {deleteResult}");

                // VERIFY DELETE
                var deleted = await store.GetAsync<IntegrationTestState>(flowId);
                results[storeName].Add($"After Delete: {deleted == null}");

                _output.WriteLine($"✓ {storeName} - All CRUD operations completed");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"✗ {storeName} - Error: {ex.Message}");
                results[storeName].Add($"Error: {ex.Message}");
            }
        }

        // Compare results
        var referenceResults = results.First().Value;
        foreach (var kvp in results.Skip(1))
        {
            kvp.Value.Should().BeEquivalentTo(referenceResults,
                $"{kvp.Key} should produce identical results to {results.First().Key}");
        }

        _output.WriteLine("✅ All stores produced identical CRUD results");
    }

    [Fact]
    public async Task RealStores_WaitConditions_AreIdentical()
    {
        if (_stores.Count == 1)
        {
            _output.WriteLine("⚠️ Skipping - Only InMemory store available");
            return;
        }

        var flowId = $"integration-wait-{Guid.NewGuid()}";

        foreach (var store in _stores)
        {
            var storeName = store.GetType().Name;

            try
            {
                // Test WhenAll
                await store.SetWaitConditionAsync(
                    flowId,
                    WaitConditionType.WhenAll,
                    new[] { "signal1", "signal2", "signal3" },
                    DateTime.UtcNow.AddMinutes(5));

                var condition = await store.GetWaitConditionAsync(flowId);
                condition.Should().NotBeNull();
                condition!.WaitingFor.Should().HaveCount(3);

                // Update signals
                var update1 = await store.UpdateWaitConditionAsync(flowId, "signal1", "result1");
                update1.Value.IsComplete.Should().BeFalse();

                var update2 = await store.UpdateWaitConditionAsync(flowId, "signal2", "result2");
                update2.Value.IsComplete.Should().BeFalse();

                var update3 = await store.UpdateWaitConditionAsync(flowId, "signal3", "result3");
                update3.Value.IsComplete.Should().BeTrue();
                update3.Value.Results.Should().HaveCount(3);

                // Clear
                await store.ClearWaitConditionAsync(flowId);

                // Test WhenAny
                await store.SetWaitConditionAsync(
                    flowId,
                    WaitConditionType.WhenAny,
                    new[] { "fast", "slow" },
                    DateTime.UtcNow.AddMinutes(5));

                var anyUpdate = await store.UpdateWaitConditionAsync(flowId, "fast", "fast-result");
                anyUpdate.Value.IsComplete.Should().BeTrue();
                anyUpdate.Value.Results.Should().HaveCount(1);

                await store.ClearWaitConditionAsync(flowId);

                _output.WriteLine($"✓ {storeName} - Wait conditions work correctly");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"✗ {storeName} - Wait condition error: {ex.Message}");
                throw;
            }
        }
    }

    [Fact]
    public async Task RealStores_ForEachProgress_IsIdentical()
    {
        if (_stores.Count == 1)
        {
            _output.WriteLine("⚠️ Skipping - Only InMemory store available");
            return;
        }

        var flowId = $"integration-foreach-{Guid.NewGuid()}";
        const int stepIndex = 5;

        foreach (var store in _stores)
        {
            var storeName = store.GetType().Name;

            try
            {
                var progress = new ForEachProgress
                {
                    FlowId = flowId,
                    StepIndex = stepIndex,
                    ProcessedItems = new HashSet<string> { "item1", "item2", "item3" },
                    FailedItems = new List<string> { "item4" },
                    CurrentBatchIndex = 2,
                    TotalBatches = 10,
                    ItemResults = new Dictionary<string, object?>
                    {
                        ["item1"] = "result1",
                        ["item2"] = 42,
                        ["item3"] = true
                    }
                };

                // Save
                var saveResult = await store.SaveForEachProgressAsync(flowId, stepIndex, progress);
                saveResult.IsSuccess.Should().BeTrue();

                // Get
                var retrieved = await store.GetForEachProgressAsync(flowId, stepIndex);
                retrieved.Should().NotBeNull();
                retrieved!.ProcessedItems.Should().HaveCount(3);
                retrieved.FailedItems.Should().HaveCount(1);
                retrieved.CurrentBatchIndex.Should().Be(2);
                retrieved.ItemResults.Should().HaveCount(3);

                // Update
                progress.ProcessedItems.Add("item5");
                progress.CurrentBatchIndex = 3;
                await store.SaveForEachProgressAsync(flowId, stepIndex, progress);

                var updated = await store.GetForEachProgressAsync(flowId, stepIndex);
                updated!.ProcessedItems.Should().Contain("item5");
                updated.CurrentBatchIndex.Should().Be(3);

                // Clear
                await store.ClearForEachProgressAsync(flowId, stepIndex);

                var cleared = await store.GetForEachProgressAsync(flowId, stepIndex);
                cleared.Should().BeNull();

                _output.WriteLine($"✓ {storeName} - ForEach progress works correctly");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"✗ {storeName} - ForEach progress error: {ex.Message}");
                throw;
            }
        }
    }

    [Fact]
    public async Task RealStores_HandleConcurrency_Identically()
    {
        if (_stores.Count == 1)
        {
            _output.WriteLine("⚠️ Skipping - Only InMemory store available");
            return;
        }

        var flowId = $"integration-concurrent-{Guid.NewGuid()}";

        foreach (var store in _stores)
        {
            var storeName = store.GetType().Name;

            // Create initial flow
            var snapshot = new FlowSnapshot<IntegrationTestState>
            {
                FlowId = flowId,
                State = new IntegrationTestState { FlowId = flowId, Counter = 0 },
                Status = DslFlowStatus.Running,
                Position = new FlowPosition(new[] { 0 }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            await store.CreateAsync(snapshot);

            // Concurrent updates
            var tasks = Enumerable.Range(1, 10).Select(i => Task.Run(async () =>
            {
                for (int retry = 0; retry < 3; retry++)
                {
                    var current = await store.GetAsync<IntegrationTestState>(flowId);
                    if (current != null)
                    {
                        current.State.Counter++;
                        var result = await store.UpdateAsync(current);
                        if (result.IsSuccess) break;
                    }
                }
            }));

            await Task.WhenAll(tasks);

            // Verify
            var final = await store.GetAsync<IntegrationTestState>(flowId);
            final.Should().NotBeNull();
            final!.State.Counter.Should().BeGreaterThan(0);
            final.Version.Should().BeGreaterThan(1);

            _output.WriteLine($"✓ {storeName} - Handled {final.State.Counter} concurrent updates, version: {final.Version}");

            // Cleanup
            await store.DeleteAsync(flowId);
        }
    }

    [Fact]
    public async Task RealStores_TimeoutDetection_IsIdentical()
    {
        if (_stores.Count == 1)
        {
            _output.WriteLine("⚠️ Skipping - Only InMemory store available");
            return;
        }

        foreach (var store in _stores)
        {
            var storeName = store.GetType().Name;

            // Create expired conditions
            var expiredIds = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var flowId = $"timeout-{storeName}-{i}";
                expiredIds.Add(flowId);
                await store.SetWaitConditionAsync(
                    flowId,
                    WaitConditionType.WhenAll,
                    new[] { "signal" },
                    DateTime.UtcNow.AddSeconds(-10)); // Already expired
            }

            // Create future conditions
            for (int i = 0; i < 2; i++)
            {
                var flowId = $"future-{storeName}-{i}";
                await store.SetWaitConditionAsync(
                    flowId,
                    WaitConditionType.WhenAll,
                    new[] { "signal" },
                    DateTime.UtcNow.AddMinutes(10)); // Future timeout
            }

            // Get timed out
            var timedOut = await store.GetTimedOutWaitConditionsAsync();
            timedOut.Select(c => c.FlowId).Should().Contain(expiredIds);

            _output.WriteLine($"✓ {storeName} - Found {timedOut.Count} timed-out conditions");

            // Cleanup
            foreach (var id in expiredIds)
            {
                await store.ClearWaitConditionAsync(id);
            }
        }
    }

    private bool CheckRedisAvailable()
    {
        try
        {
            using var connection = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false,connectTimeout=1000");
            return connection.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckNatsAvailable()
    {
        try
        {
            var opts = new NatsOpts { Url = "nats://localhost:4222", ConnectTimeout = TimeSpan.FromSeconds(1) };
            using var connection = new NatsConnection(opts);
            connection.ConnectAsync().Wait(1000);
            return connection.ConnectionState == NatsConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    private async Task CreateNatsBucketsAsync()
    {
        if (_jetStream == null) return;

        try
        {
            // Create flow bucket
            var flowConfig = new StreamConfig
            {
                Name = "KV_integration",
                Subjects = new[] { "$KV.integration.>" },
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Limits,
                MaxAge = TimeSpan.FromDays(7)
            };

            await _jetStream.CreateStreamAsync(flowConfig);

            // Create wait bucket
            var waitConfig = new StreamConfig
            {
                Name = "KV_integration_wait",
                Subjects = new[] { "$KV.integration_wait.>" },
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Limits,
                MaxAge = TimeSpan.FromDays(1)
            };

            await _jetStream.CreateStreamAsync(waitConfig);
        }
        catch
        {
            // Buckets might already exist
        }
    }
}

// Test state for integration testing
public class StorageIntegrationState : IFlowState
{
    public string? FlowId { get; set; }
    public string Data { get; set; } = string.Empty;
    public int Counter { get; set; }
    public List<string> Items { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Simple JSON serializer for integration tests
public class IntegrationTestSerializer : IMessageSerializer
{
    public string Name => "integration-json";

    public byte[] Serialize<T>(T value)
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeof(T));
    }

    public T Deserialize<T>(byte[] data)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(data)!;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(data)!;
    }

    public void Serialize<T>(T value, IBufferWriter<byte> bufferWriter)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeof(T));
        bufferWriter.Write(bytes);
    }

    public byte[] Serialize(object value, Type type)
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
    }

    public object? Deserialize(byte[] data, Type type)
    {
        return System.Text.Json.JsonSerializer.Deserialize(data, type);
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        return System.Text.Json.JsonSerializer.Deserialize(data, type);
    }

    public void Serialize(object value, Type type, IBufferWriter<byte> bufferWriter)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        bufferWriter.Write(bytes);
    }
}
