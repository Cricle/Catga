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
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using StackExchange.Redis;
using NATS.Client.Core;

namespace Catga.Tests.Flow;

/// <summary>
/// Comprehensive parity tests for InMemory, Redis, and NATS storage implementations.
/// Ensures all three stores have identical functionality and behavior.
/// </summary>
public class StorageParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDslFlowStore> _stores = new();
    private IConnectionMultiplexer? _redisConnection;
    private INatsConnection? _natsConnection;

    public StorageParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Setup InMemory store
        _stores.Add(TestStoreExtensions.CreateTestFlowStore());

        // Setup Redis store (mock for unit tests)
        _redisConnection = Substitute.For<IConnectionMultiplexer>();
        var redisDb = Substitute.For<IDatabase>();
        _redisConnection.GetDatabase(Arg.Any<int>()).Returns(redisDb);

        var serializer = new TestSerializer();
        _stores.Add(new RedisDslFlowStore(_redisConnection, serializer, "test:"));

        // Setup NATS store (mock for unit tests)
        _natsConnection = Substitute.For<INatsConnection>();
        _stores.Add(new NatsDslFlowStore(_natsConnection, serializer, "test-bucket"));
    }

    public Task DisposeAsync()
    {
        _redisConnection?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AllStores_CreateAsync_BehavesIdentically()
    {
        // Arrange
        var flowId = "parity-create-001";
        var state = new ParityTestState
        {
            FlowId = flowId,
            Value = 42,
            Text = "test"
        };

        // Act & Assert for each store
        foreach (var store in _stores)
        {
            var snapshot = new FlowSnapshot<ParityTestState>
            {
                FlowId = flowId,
                State = state,
                Status = DslFlowStatus.Running,
                Position = new FlowPosition(new[] { 0 }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            // Create should succeed
            var result = await store.CreateAsync(snapshot);
            result.Should().BeTrue($"{store.GetType().Name} should create successfully");

            // Duplicate create should fail
            var duplicateResult = await store.CreateAsync(snapshot);
            duplicateResult.Should().BeFalse($"{store.GetType().Name} should reject duplicates");

            _output.WriteLine($"✓ {store.GetType().Name} - CreateAsync works correctly");
        }
    }

    [Fact]
    public async Task AllStores_GetAsync_BehavesIdentically()
    {
        // Arrange
        var flowId = "parity-get-001";
        var state = new ParityTestState
        {
            FlowId = flowId,
            Value = 100,
            Items = new List<string> { "a", "b", "c" }
        };

        foreach (var store in _stores)
        {
            var snapshot = new FlowSnapshot<ParityTestState>
            {
                FlowId = flowId,
                State = state,
                Status = DslFlowStatus.Completed,
                Position = new FlowPosition(new[] { 3, 2, 1 }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 5
            };

            // Create first
            await store.CreateAsync(snapshot);

            // Act - Get existing
            var retrieved = await store.GetAsync<ParityTestState>(flowId);

            // Assert
            retrieved.Should().NotBeNull($"{store.GetType().Name} should retrieve existing flow");
            retrieved!.FlowId.Should().Be(flowId);
            retrieved.State.Value.Should().Be(100);
            retrieved.State.Items.Should().BeEquivalentTo(new[] { "a", "b", "c" });
            retrieved.Status.Should().Be(DslFlowStatus.Completed);
            retrieved.Position.Path.Should().BeEquivalentTo(new[] { 3, 2, 1 });

            // Act - Get non-existing
            var notFound = await store.GetAsync<ParityTestState>("non-existing-id");
            notFound.Should().BeNull($"{store.GetType().Name} should return null for non-existing");

            _output.WriteLine($"✓ {store.GetType().Name} - GetAsync works correctly");
        }
    }

    [Fact]
    public async Task AllStores_UpdateAsync_BehavesIdentically()
    {
        // Arrange
        var flowId = "parity-update-001";
        var initialState = new ParityTestState { FlowId = flowId, Value = 1 };
        var updatedState = new ParityTestState { FlowId = flowId, Value = 2 };

        foreach (var store in _stores)
        {
            var snapshot = new FlowSnapshot<ParityTestState>
            {
                FlowId = flowId,
                State = initialState,
                Status = DslFlowStatus.Running,
                Position = new FlowPosition(new[] { 0 }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            await store.CreateAsync(snapshot);

            // Act - Update with correct version
            snapshot = snapshot with
            {
                State = updatedState,
                Status = DslFlowStatus.Completed,
                Version = 1,
                Position = new FlowPosition(new[] { 5 })
            };

            var updateResult = await store.UpdateAsync(snapshot);

            // Assert
            updateResult.Should().BeTrue($"{store.GetType().Name} should update with correct version");

            var retrieved = await store.GetAsync<ParityTestState>(flowId);
            retrieved!.State.Value.Should().Be(2);
            retrieved.Status.Should().Be(DslFlowStatus.Completed);
            retrieved.Version.Should().Be(2); // Version incremented
            retrieved.Position.Path.Should().BeEquivalentTo(new[] { 5 });

            // Act - Update with wrong version (optimistic locking)
            var wrongVersionSnapshot = retrieved with { Version = 1 };
            var wrongVersionResult = await store.UpdateAsync(wrongVersionSnapshot);
            wrongVersionResult.Should().BeFalse($"{store.GetType().Name} should reject wrong version");

            _output.WriteLine($"✓ {store.GetType().Name} - UpdateAsync with optimistic locking works");
        }
    }

    [Fact]
    public async Task AllStores_DeleteAsync_BehavesIdentically()
    {
        // Arrange
        var flowId = "parity-delete-001";

        foreach (var store in _stores)
        {
            var snapshot = new FlowSnapshot<ParityTestState>
            {
                FlowId = flowId,
                State = new ParityTestState { FlowId = flowId },
                Status = DslFlowStatus.Completed,
                Position = new FlowPosition(new[] { 0 }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            await store.CreateAsync(snapshot);

            // Act - Delete existing
            var deleteResult = await store.DeleteAsync(flowId);
            deleteResult.Should().BeTrue($"{store.GetType().Name} should delete existing flow");

            // Verify deleted
            var retrieved = await store.GetAsync<ParityTestState>(flowId);
            retrieved.Should().BeNull($"{store.GetType().Name} should not find deleted flow");

            // Act - Delete non-existing
            var deleteNonExisting = await store.DeleteAsync("non-existing");
            deleteNonExisting.Should().BeFalse($"{store.GetType().Name} should return false for non-existing");

            _output.WriteLine($"✓ {store.GetType().Name} - DeleteAsync works correctly");
        }
    }

    [Fact]
    public async Task AllStores_WaitConditions_BehaveIdentically()
    {
        // Arrange
        var flowId = "parity-wait-001";
        var timeout = DateTime.UtcNow.AddMinutes(5);

        foreach (var store in _stores)
        {
            // Test SetWaitConditionAsync (compat shim)
            await store.SetWaitConditionAsync(
                flowId,
                WaitConditionType.WhenAll,
                new[] { "signal1", "signal2", "signal3" },
                timeout);

            // Test GetWaitConditionAsync
            var condition = await store.GetWaitConditionAsync(flowId);
            condition.Should().NotBeNull($"{store.GetType().Name} should get wait condition");
            condition!.FlowId.Should().Be(flowId);
            condition.Type.Should().Be(WaitType.All);
            condition.ExpectedCount.Should().Be(3);
            condition.CompletedCount.Should().Be(0);
            condition.ChildFlowIds.Should().BeEquivalentTo(new[] { "signal1", "signal2", "signal3" });

            // Test UpdateWaitConditionAsync - complete one signal
            var afterFirst = await store.UpdateWaitConditionAsync(flowId, "signal1", "result1");
            afterFirst.Should().NotBeNull();
            afterFirst!.CompletedCount.Should().Be(1);
            afterFirst.Results.Should().HaveCount(1);

            // Check if completed (WhenAll needs all signals)
            afterFirst.CompletedCount.Should().BeLessThan(afterFirst.ExpectedCount);

            // Complete remaining signals
            await store.UpdateWaitConditionAsync(flowId, "signal2", "result2");
            var finalUpdate = await store.UpdateWaitConditionAsync(flowId, "signal3", "result3");
            finalUpdate.Should().NotBeNull();
            finalUpdate!.CompletedCount.Should().Be(finalUpdate.ExpectedCount);
            finalUpdate.Results.Should().HaveCount(3);

            // Test ClearWaitConditionAsync
            await store.ClearWaitConditionAsync(flowId);

            var clearedCondition = await store.GetWaitConditionAsync(flowId);
            clearedCondition.Should().BeNull($"{store.GetType().Name} should not find cleared condition");

            _output.WriteLine($"✓ {store.GetType().Name} - WaitCondition operations work correctly");
        }
    }

    [Fact]
    public async Task AllStores_WhenAnyCondition_BehavesIdentically()
    {
        // Arrange
        var flowId = "parity-whenany-001";
        var timeout = DateTime.UtcNow.AddMinutes(5);

        foreach (var store in _stores)
        {
            // Set WhenAny condition (compat shim)
            await store.SetWaitConditionAsync(
                flowId,
                WaitConditionType.WhenAny,
                new[] { "fast", "medium", "slow" },
                timeout);

            // Update one signal - should complete immediately for WhenAny
            var updated = await store.UpdateWaitConditionAsync(flowId, "fast", "fast-result");

            updated.Should().NotBeNull();
            updated!.CompletedCount.Should().Be(1);
            updated.Results.Should().HaveCount(1);
            updated.Results[0].FlowId.Should().Be("fast");
            updated.Results[0].Result.Should().Be("fast-result");

            // Clear for next test
            await store.ClearWaitConditionAsync(flowId);

            _output.WriteLine($"✓ {store.GetType().Name} - WhenAny condition works correctly");
        }
    }

    [Fact]
    public async Task AllStores_GetTimedOutConditions_BehavesIdentically()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var pastTimeout = now.AddMinutes(-5);
        var futureTimeout = now.AddMinutes(5);

        foreach (var store in _stores)
        {
            // Create conditions with different timeouts (compat shim)
            await store.SetWaitConditionAsync("timeout-past-1", WaitConditionType.WhenAll,
                new[] { "signal1" }, pastTimeout);
            await store.SetWaitConditionAsync("timeout-past-2", WaitConditionType.WhenAny,
                new[] { "signal2" }, pastTimeout);
            await store.SetWaitConditionAsync("timeout-future-1", WaitConditionType.WhenAll,
                new[] { "signal3" }, futureTimeout);

            // Act
            var timedOut = await store.GetTimedOutWaitConditionsAsync();

            // Assert
            timedOut.Should().HaveCount(2, $"{store.GetType().Name} should find 2 timed-out conditions");
            timedOut.Select(c => c.FlowId).Should().BeEquivalentTo(new[] { "timeout-past-1", "timeout-past-2" });

            // Cleanup
            await store.ClearWaitConditionAsync("timeout-past-1");
            await store.ClearWaitConditionAsync("timeout-past-2");
            await store.ClearWaitConditionAsync("timeout-future-1");

            _output.WriteLine($"✓ {store.GetType().Name} - GetTimedOutConditions works correctly");
        }
    }

    [Fact]
    public async Task AllStores_ForEachProgress_BehavesIdentically()
    {
        // Arrange
        var flowId = "parity-foreach-001";
        var stepIndex = 5;

        foreach (var store in _stores)
        {
            var progress = new ForEachProgress
            {
                CurrentIndex = 3,
                TotalCount = 5,
                CompletedIndices = new List<int> { 0, 1, 2 },
                FailedIndices = new List<int> { 4 }
            };

            // Save progress
            await store.SaveForEachProgressAsync(flowId, stepIndex, progress);

            // Get progress
            var retrieved = await store.GetForEachProgressAsync(flowId, stepIndex);
            retrieved.Should().NotBeNull($"{store.GetType().Name} should retrieve ForEach progress");
            retrieved!.CurrentIndex.Should().Be(3);
            retrieved.TotalCount.Should().Be(5);
            retrieved.CompletedIndices.Should().BeEquivalentTo(new[] { 0, 1, 2 });
            retrieved.FailedIndices.Should().BeEquivalentTo(new[] { 4 });

            // Update progress
            var updatedProgress = progress with
            {
                CurrentIndex = 5,
                CompletedIndices = new List<int> { 0, 1, 2, 3, 4 },
                FailedIndices = new List<int>()
            };

            await store.SaveForEachProgressAsync(flowId, stepIndex, updatedProgress);

            var updated = await store.GetForEachProgressAsync(flowId, stepIndex);
            updated.Should().NotBeNull();
            updated!.CurrentIndex.Should().Be(5);
            updated.CompletedIndices.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
            updated.FailedIndices.Should().BeEmpty();

            // Clear progress
            await store.ClearForEachProgressAsync(flowId, stepIndex);

            var cleared = await store.GetForEachProgressAsync(flowId, stepIndex);
            cleared.Should().BeNull($"{store.GetType().Name} should not find cleared progress");

            _output.WriteLine($"✓ {store.GetType().Name} - ForEachProgress operations work correctly");
        }
    }

    [Fact]
    public async Task AllStores_HandleConcurrentOperations_Identically()
    {
        // Arrange
        var flowId = "parity-concurrent-001";
        var tasks = new List<Task>();

        foreach (var store in _stores)
        {
            var snapshot = new FlowSnapshot<ParityTestState>
            {
                FlowId = flowId,
                State = new ParityTestState { FlowId = flowId, Value = 0 },
                Status = DslFlowStatus.Running,
                Position = new FlowPosition(new[] { 0 }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            await store.CreateAsync(snapshot);

            // Simulate concurrent updates
            var updateTasks = Enumerable.Range(1, 10).Select(i => Task.Run(async () =>
            {
                var current = await store.GetAsync<ParityTestState>(flowId);
                if (current != null)
                {
                    current.State.Value = i;
                    await store.UpdateAsync(current);
                }
            }));

            await Task.WhenAll(updateTasks);

            // Verify final state
            var final = await store.GetAsync<ParityTestState>(flowId);
            final.Should().NotBeNull();
            final!.Version.Should().BeGreaterThan(1, "Version should increment with updates");

            _output.WriteLine($"✓ {store.GetType().Name} - Handles concurrent operations");
        }
    }

    [Fact]
    public async Task AllStores_HandleLargePayloads_Identically()
    {
        // Arrange
        var flowId = "parity-large-001";
        var largeList = Enumerable.Range(1, 10000).Select(i => $"Item_{i}").ToList();
        var largeDict = Enumerable.Range(1, 1000)
            .ToDictionary(i => $"Key_{i}", i => $"Value_{i}_{new string('x', 100)}");

        foreach (var store in _stores)
        {
            var state = new ParityTestState
            {
                FlowId = flowId,
                Items = largeList,
                Data = largeDict,
                Value = 999999
            };

            var snapshot = new FlowSnapshot<ParityTestState>
            {
                FlowId = flowId,
                State = state,
                Status = DslFlowStatus.Running,
                Position = new FlowPosition(Enumerable.Range(0, 100).ToArray()), // Deep nesting
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            // Act
            var createResult = await store.CreateAsync(snapshot);
            createResult.Should().BeTrue($"{store.GetType().Name} should handle large payloads");

            var retrieved = await store.GetAsync<ParityTestState>(flowId);
            retrieved.Should().NotBeNull();
            retrieved!.State.Items.Should().HaveCount(10000);
            retrieved.State.Data.Should().HaveCount(1000);
            retrieved.Position.Path.Should().HaveCount(100);

            _output.WriteLine($"✓ {store.GetType().Name} - Handles large payloads correctly");
        }
    }

    [Fact]
    public async Task AllStores_HandleSpecialCharacters_Identically()
    {
        // Test various special characters in IDs and data
        var specialIds = new[]
        {
            "flow-with-dash",
            "flow.with.dots",
            "flow_with_underscore",
            "flow:with:colons",
            "flow/with/slashes",
            "flow\\with\\backslashes",
            "flow with spaces",
            "flow'with'quotes",
            "flow\"with\"doublequotes",
            "flow[with]brackets",
            "flow{with}braces"
        };

        foreach (var store in _stores)
        {
            foreach (var flowId in specialIds)
            {
                var state = new ParityTestState
                {
                    FlowId = flowId,
                    Text = $"Special: !@#$%^&*()_+-=[]{{}}\\|;':\",./<>?",
                    Items = new List<string> { "item:1", "item/2", "item\\3", "item\"4" }
                };

                var snapshot = new FlowSnapshot<ParityTestState>
                {
                    FlowId = flowId,
                    State = state,
                    Status = DslFlowStatus.Running,
                    Position = new FlowPosition(new[] { 0 }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Version = 1
                };

                // Create
                var createResult = await store.CreateAsync(snapshot);
                createResult.Should().BeTrue(
                    $"{store.GetType().Name} should handle special ID: {flowId}");

                // Retrieve
                var retrieved = await store.GetAsync<ParityTestState>(flowId);
                retrieved.Should().NotBeNull();
                retrieved!.State.Text.Should().Be(state.Text);
                retrieved.State.Items.Should().BeEquivalentTo(state.Items);

                // Delete
                await store.DeleteAsync(flowId);
            }

            _output.WriteLine($"✓ {store.GetType().Name} - Handles special characters correctly");
        }
    }
}

// Test state for parity testing
public class ParityTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
    public Dictionary<string, string> Data { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Simple test serializer
public class TestSerializer : IMessageSerializer
{
    public string Name => "test-json";

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
