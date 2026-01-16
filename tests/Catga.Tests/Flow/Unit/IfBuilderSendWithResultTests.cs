using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;
using Catga.Tests.Helpers;

namespace Catga.Tests.Flow.Unit;

/// <summary>
/// Tests for IIfBuilder.Send with result - verifying typed Send works correctly
/// </summary>
public class IfBuilderSendWithResultTests
{
    [Fact]
    public async Task Send_WithTypedRequest_ShouldExecuteAndSetResult()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TypedSendIfFlow();

        var state = new TypedSendTestState
        {
            FlowId = "typed-send-test",
            OrderId = "order-123",
            ShouldValidate = true
        };

        // Setup mediator to return a result for GetOrderQuery
        mediator.SendAsync<GetOrderTestQuery, OrderTestResult?>(
            Arg.Any<GetOrderTestQuery>(), 
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var query = callInfo.Arg<GetOrderTestQuery>();
                return new ValueTask<CatgaResult<OrderTestResult?>>(
                    CatgaResult<OrderTestResult?>.Success(new OrderTestResult 
                    { 
                        OrderId = query.OrderId, 
                        Status = "Valid" 
                    }));
            });

        var executor = new DslFlowExecutor<TypedSendTestState, TypedSendIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("typed send flow should succeed");
        state.IsValidated.Should().BeTrue("result should be set via Into()");
        state.ValidationResult.Should().NotBeNull();
        state.ValidationResult!.Status.Should().Be("Valid");
    }

    [Fact]
    public async Task Send_WithTypedRequest_FullFlowSimulation_ShouldWork()
    {
        // This test simulates the full OrderFulfillmentFlow scenario
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new FullFlowSimulationFlow();

        var state = new FullFlowTestState
        {
            FlowId = "full-flow-test",
            CustomerId = "CUST-001"
        };

        // Setup CreateOrder to succeed
        mediator.SendAsync<CreateTestOrderCommand, CreateTestOrderResult>(
            Arg.Any<CreateTestOrderCommand>(), 
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                return new ValueTask<CatgaResult<CreateTestOrderResult>>(
                    CatgaResult<CreateTestOrderResult>.Success(new CreateTestOrderResult 
                    { 
                        OrderId = "order-abc",
                        Total = 100m
                    }));
            });

        // Setup GetOrder to succeed (in If branch)
        mediator.SendAsync<GetOrderTestQuery, OrderTestResult?>(
            Arg.Any<GetOrderTestQuery>(), 
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var query = callInfo.Arg<GetOrderTestQuery>();
                return new ValueTask<CatgaResult<OrderTestResult?>>(
                    CatgaResult<OrderTestResult?>.Success(new OrderTestResult 
                    { 
                        OrderId = query.OrderId, 
                        Status = "Valid" 
                    }));
            });

        // Setup PayOrder to succeed
        mediator.SendAsync<PayTestOrderCommand>(
            Arg.Any<PayTestOrderCommand>(), 
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        var executor = new DslFlowExecutor<FullFlowTestState, FullFlowSimulationFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue($"full flow should succeed, but got error: {result.Error}");
        state.OrderId.Should().Be("order-abc");
        state.Total.Should().Be(100m);
        state.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task Send_WithTypedRequest_InNestedIf_ShouldWork()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NestedTypedSendFlow();

        var state = new TypedSendTestState
        {
            FlowId = "nested-typed-send",
            OrderId = "order-789",
            ShouldValidate = true,
            Amount = 1500 // High value order
        };

        mediator.SendAsync<GetOrderTestQuery, OrderTestResult?>(
            Arg.Any<GetOrderTestQuery>(), 
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<OrderTestResult?>>(
                CatgaResult<OrderTestResult?>.Success(new OrderTestResult 
                { 
                    OrderId = state.OrderId, 
                    Status = "HighValue" 
                })));

        var executor = new DslFlowExecutor<TypedSendTestState, NestedTypedSendFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue();
        state.IsValidated.Should().BeTrue();
        state.ValidationResult.Should().NotBeNull();
    }

    [Fact]
    public async Task Send_WithTypedRequest_WhenConditionFalse_ShouldSkipBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TypedSendIfFlow();

        var state = new TypedSendTestState
        {
            FlowId = "skip-branch-test",
            OrderId = "order-skip",
            ShouldValidate = false // Condition is false
        };

        var executor = new DslFlowExecutor<TypedSendTestState, TypedSendIfFlow>(mediator, store, config);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue();
        state.IsValidated.Should().BeFalse("branch should be skipped");
        state.ValidationResult.Should().BeNull("no result should be set");
        
        // Verify mediator was never called
        await mediator.DidNotReceive().SendAsync<GetOrderTestQuery, OrderTestResult?>(
            Arg.Any<GetOrderTestQuery>(), 
            Arg.Any<CancellationToken>());
    }
}

// Test Flow using typed Send<TRequest, TResult>
public class TypedSendIfFlow : FlowConfig<TypedSendTestState>
{
    protected override void Configure(IFlowBuilder<TypedSendTestState> flow)
    {
        flow.Name("typed-send-if-flow");

        flow.If(s => s.ShouldValidate)
            .Send<GetOrderTestQuery, OrderTestResult?>(s => new GetOrderTestQuery(s.OrderId))
            .Into((state, result) =>
            {
                state.IsValidated = result != null;
                state.ValidationResult = result;
            })
            .EndIf();
    }
}

// Nested If with typed Send
public class NestedTypedSendFlow : FlowConfig<TypedSendTestState>
{
    protected override void Configure(IFlowBuilder<TypedSendTestState> flow)
    {
        flow.Name("nested-typed-send-flow");

        flow.If(s => s.ShouldValidate)
            .If(s => s.Amount > 1000)
                .Send<GetOrderTestQuery, OrderTestResult?>(s => new GetOrderTestQuery(s.OrderId))
                .Into((state, result) =>
                {
                    state.IsValidated = result != null;
                    state.ValidationResult = result;
                })
                .EndIf()
            .EndIf();
    }
}

// Test State
public class TypedSendTestState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public bool ShouldValidate { get; set; }
    public decimal Amount { get; set; }
    public bool IsValidated { get; set; }
    public OrderTestResult? ValidationResult { get; set; }

    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Test Query and Result
public record GetOrderTestQuery(string OrderId) : IRequest<OrderTestResult?>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class OrderTestResult
{
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

// Full flow simulation types
public class FullFlowSimulationFlow : FlowConfig<FullFlowTestState>
{
    protected override void Configure(IFlowBuilder<FullFlowTestState> flow)
    {
        flow.Name("full-flow-simulation");

        // Step 1: Create order
        flow.Send<FullFlowTestState, CreateTestOrderCommand, CreateTestOrderResult>(
            state => new CreateTestOrderCommand(state.CustomerId))
            .Into((state, result) =>
            {
                state.OrderId = result.OrderId;
                state.Total = result.Total;
            });

        // Step 2: Validate order (conditional) - using typed Send in If branch
        flow.If(state => state.Total > 0)
            .Send<GetOrderTestQuery, OrderTestResult?>(state => new GetOrderTestQuery(state.OrderId))
            .Into((state, order) => state.IsValidated = order != null)
            .EndIf();

        // Step 3: Pay order
        flow.Send(state => new PayTestOrderCommand(state.OrderId));
    }
}

public class FullFlowTestState : IFlowState
{
    public string? FlowId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public bool IsValidated { get; set; }

    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public record CreateTestOrderCommand(string CustomerId) : IRequest<CreateTestOrderResult>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class CreateTestOrderResult
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public record PayTestOrderCommand(string OrderId) : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
