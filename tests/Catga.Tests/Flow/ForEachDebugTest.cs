using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

public class ForEachDebugTest
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

    public class TestFlow : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Name("test-debug-flow");

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
    public async Task Debug_ForEach_ShouldWork()
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

        var store = new InMemoryDslFlowStore();
        var config = new TestFlow();
        config.Build();

        var executor = new DslFlowExecutor<TestState, TestFlow>(mediator, store, config);

        var state = new TestState
        {
            FlowId = "debug-001",
            Items = ["item1", "item2", "item3"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(3);
        result.State.Results.Should().ContainKeys("item1", "item2", "item3");
    }
}
