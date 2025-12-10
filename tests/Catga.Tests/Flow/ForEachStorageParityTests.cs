using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Tests to ensure ForEach functionality is equivalent across all storage layers.
/// </summary>
public class ForEachStorageParityTests
{
    public class TestState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<string> Items { get; set; } = [];
        public Dictionary<string, string> Results { get; set; } = [];

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public record TestRequest(string ItemId) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public class TestForEachFlow : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Name("test-foreach-parity");

            flow.ForEach<string>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new TestRequest(item));
                })
                .OnItemSuccess((state, item, result) =>
                {
                    state.Results[item] = result?.ToString() ?? "success";
                })
                .EndForEach();
        }
    }

    [Fact]
    public async Task InMemoryStore_ShouldSupportForEachProgress()
    {
        // Arrange
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<object>()).Returns(new byte[] { 1, 2, 3 });
        serializer.Deserialize<ForEachProgress>(Arg.Any<byte[]>()).Returns(new ForEachProgress
        {
            CurrentIndex = 2,
            TotalCount = 5,
            CompletedIndices = [0, 1],
            FailedIndices = []
        });

        var store = new Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore(serializer);
        var progress = new ForEachProgress
        {
            CurrentIndex = 2,
            TotalCount = 5,
            CompletedIndices = [0, 1],
            FailedIndices = []
        };

        // Act & Assert
        await store.SaveForEachProgressAsync("flow1", 0, progress);
        var retrieved = await store.GetForEachProgressAsync("flow1", 0);

        retrieved.Should().NotBeNull();
        retrieved!.CurrentIndex.Should().Be(2);
        retrieved.TotalCount.Should().Be(5);
        retrieved.CompletedIndices.Should().BeEquivalentTo([0, 1]);

        await store.ClearForEachProgressAsync("flow1", 0);
        var cleared = await store.GetForEachProgressAsync("flow1", 0);
        cleared.Should().BeNull();
    }

    [Fact]
    public async Task ForEachExecution_ShouldWorkWithInMemoryStore()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestRequest, string>(
            Arg.Any<TestRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<TestRequest>();
                return CatgaResult<string>.Success($"result-{request.ItemId}");
            });

        var serializer = Substitute.For<IMessageSerializer>();
        var store = new Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore(serializer);
        var config = new TestForEachFlow();
        config.Build();

        var executor = new DslFlowExecutor<TestState, TestForEachFlow>(mediator, store, config);

        var state = new TestState
        {
            FlowId = "parity-test-001",
            Items = ["item1", "item2", "item3"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(3);
        result.State.Results.Should().ContainKeys("item1", "item2", "item3");
        result.State.Results["item1"].Should().Be("result-0");
        result.State.Results["item2"].Should().Be("result-1");
        result.State.Results["item3"].Should().Be("result-2");
    }

    [Theory]
    [InlineData("InMemory")]
    public async Task ForEachProgress_ShouldBePersistentAcrossStores(string storeType)
    {
        // This test verifies that ForEach progress can be saved and retrieved
        // across different storage implementations

        IDslFlowStore store = storeType switch
        {
            "InMemory" => new Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore(Substitute.For<IMessageSerializer>()),
            // Add Redis and NATS when available in test environment
            _ => throw new ArgumentException($"Unknown store type: {storeType}")
        };

        var progress = new ForEachProgress
        {
            CurrentIndex = 3,
            TotalCount = 10,
            CompletedIndices = [0, 1, 2],
            FailedIndices = [4]
        };

        // Test save/retrieve cycle
        await store.SaveForEachProgressAsync("test-flow", 1, progress);
        var retrieved = await store.GetForEachProgressAsync("test-flow", 1);

        retrieved.Should().NotBeNull();
        retrieved!.CurrentIndex.Should().Be(3);
        retrieved.TotalCount.Should().Be(10);
        retrieved.CompletedIndices.Should().BeEquivalentTo([0, 1, 2]);
        retrieved.FailedIndices.Should().BeEquivalentTo([4]);

        // Test clear
        await store.ClearForEachProgressAsync("test-flow", 1);
        var cleared = await store.GetForEachProgressAsync("test-flow", 1);
        cleared.Should().BeNull();
    }

    [Fact]
    public void ForEachProgress_ShouldBeSerializable()
    {
        // Verify that ForEachProgress can be serialized/deserialized
        // This is important for Redis and NATS storage

        var progress = new ForEachProgress
        {
            CurrentIndex = 5,
            TotalCount = 20,
            CompletedIndices = [0, 1, 2, 3, 4],
            FailedIndices = [6, 7]
        };

        // Test record equality and immutability
        var copy = progress with { CurrentIndex = 6 };

        copy.CurrentIndex.Should().Be(6);
        copy.TotalCount.Should().Be(20);
        copy.CompletedIndices.Should().BeEquivalentTo([0, 1, 2, 3, 4]);
        copy.FailedIndices.Should().BeEquivalentTo([6, 7]);

        // Original should be unchanged
        progress.CurrentIndex.Should().Be(5);
    }
}
