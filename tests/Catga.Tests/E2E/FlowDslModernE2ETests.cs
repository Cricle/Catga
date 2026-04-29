using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using Catga.Persistence.InMemory.Flow;
using Catga.Testing;
using Catga.Tests.Flow.TDD;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.E2E;

public class FlowDslModernE2ETests
{
    [Fact]
    public async Task RegisteredFlow_RunAsync_CompletesSequentialBranchingFlow()
    {
        var mediator = new MockMediator()
            .OnSend<StartFlowCommand, string>(_ => "started")
            .OnSend<ApproveOrderCommand, string>(request => $"approval:{request.Level}")
            .OnSend<RouteOrderCommand, string>(request => $"route:{request.Region}");

        var services = new ServiceCollection();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddFlowDsl();
        services.AddFlow<ModernOrderState, ModernOrderFlow>(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        var executor = provider.CreateFlowExecutor<ModernOrderState, ModernOrderFlow>();

        var state = new ModernOrderState
        {
            FlowId = "modern-flow-001",
            Amount = 180,
            Region = "eu"
        };

        var result = await executor.RunAsync(state);

        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(DslFlowStatus.Completed);
        result.State!.Started.Should().BeTrue();
        result.State.Approval.Should().Be("approval:high");
        result.State.Route.Should().Be("route:eu");
    }

    [Fact]
    public async Task Flow_RunAsync_ForEach_ProcessesAllItemsAndCompletes()
    {
        await using var ctx = new FlowTestContext<BatchState, BatchFlow>();
        ctx.Mediator.OnSend<ProcessBatchItemCommand, string>(request => $"done:{request.Item}");

        var result = await ctx.RunAsync(new BatchState
        {
            FlowId = "batch-flow-001",
            Items = ["A", "B", "C"]
        });

        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(DslFlowStatus.Completed);
        result.State!.ProcessedItems.Should().Equal("A", "B", "C");
        result.State.Results.Should().Equal("done:A", "done:B", "done:C");
        result.State.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task Flow_RunAsync_OnlyWhen_SkipsConditionalSend()
    {
        await using var ctx = new FlowTestContext<NotificationState, ConditionalNotificationFlow>();
        ctx.Mediator.OnSend<MarkReadyCommand, bool>(_ => true);
        ctx.Mediator.OnSend<SendNotificationCommand, string>(_ => "sent");

        var result = await ctx.RunAsync(new NotificationState
        {
            FlowId = "notify-flow-001",
            ShouldNotify = false
        });

        result.IsSuccess.Should().BeTrue();
        result.State!.IsReady.Should().BeTrue();
        result.State.NotificationResult.Should().BeNull();
        ctx.Mediator.Sent.Should().ContainSingle(x => x is MarkReadyCommand);
        ctx.Mediator.Sent.Should().NotContain(x => x is SendNotificationCommand);
    }

    [Fact]
    public async Task Flow_RunAsync_RemoteSend_UsesRequestClientFactory()
    {
        var requestClientFactory = new TestRequestClientFactory()
            .OnRequest<CheckRemoteStockCommand, RemoteStockResult>((request, _, _) =>
                CatgaResult<RemoteStockResult>.Success(new RemoteStockResult(request.Sku, 8, true)))
            .OnRequest<ReserveRemoteStockCommand, ReservationResult>((request, _, _) =>
                CatgaResult<ReservationResult>.Success(new ReservationResult($"res:{request.Sku}:{request.Quantity}")));

        await using var ctx = new FlowTestContext<RemoteCheckoutState, RemoteCheckoutFlow>(
            services => services.AddSingleton<IRequestClientFactory>(requestClientFactory));

        var result = await ctx.RunAsync(new RemoteCheckoutState
        {
            FlowId = "remote-flow-001",
            Sku = "SKU-001",
            Quantity = 3
        });

        result.IsSuccess.Should().BeTrue();
        result.State!.InStock.Should().BeTrue();
        result.State.AvailableQuantity.Should().Be(8);
        result.State.ReservationId.Should().Be("res:SKU-001:3");
        requestClientFactory.Requests.Should().HaveCount(2);
        ctx.Mediator.Sent.Should().BeEmpty();
    }
}

public sealed class ModernOrderState : IFlowState
{
    public string? FlowId { get; set; }
    public int Amount { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool Started { get; set; }
    public string? Approval { get; set; }
    public string? Route { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public sealed class ModernOrderFlow : FlowConfig<ModernOrderState>
{
    protected override void Configure(IFlowBuilder<ModernOrderState> flow)
    {
        flow.Name("modern-order-flow");

        flow.Send<ModernOrderState, StartFlowCommand, string>(s => new StartFlowCommand())
            .Into((state, result) => state.Started = result == "started");

        flow.If(s => s.Amount >= 100)
            .Send<ApproveOrderCommand, string>(s => new ApproveOrderCommand("high"))
            .Into((state, result) => state.Approval = result)
        .Else()
            .Send<ApproveOrderCommand, string>(s => new ApproveOrderCommand("normal"))
            .Into((state, result) => state.Approval = result)
        .EndIf();

        flow.Switch(s => s.Region)
            .Case("eu", branch => branch.Send<RouteOrderCommand, string>(s => new RouteOrderCommand("eu"))
                .Into((state, result) => state.Route = result))
            .Case("us", branch => branch.Send<RouteOrderCommand, string>(s => new RouteOrderCommand("us"))
                .Into((state, result) => state.Route = result))
            .Default(branch => branch.Send<RouteOrderCommand, string>(s => new RouteOrderCommand("default"))
                .Into((state, result) => state.Route = result))
            .EndSwitch();
    }
}

public record StartFlowCommand : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ApproveOrderCommand(string Level) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record RouteOrderCommand(string Region) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class BatchState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = [];
    public List<string> ProcessedItems { get; set; } = [];
    public List<string> Results { get; set; } = [];
    public bool Completed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public sealed class BatchFlow : FlowConfig<BatchState>
{
    protected override void Configure(IFlowBuilder<BatchState> flow)
    {
        flow.Name("batch-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send<BatchState, ProcessBatchItemCommand, string>(s => new ProcessBatchItemCommand(item));
            })
            .OnItemSuccess((state, item, result) =>
            {
                state.ProcessedItems.Add(item);
                state.Results.Add((string)result);
            })
            .OnComplete(state => state.Completed = true)
            .EndForEach();
    }
}

public record ProcessBatchItemCommand(string Item) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class NotificationState : IFlowState
{
    public string? FlowId { get; set; }
    public bool ShouldNotify { get; set; }
    public bool IsReady { get; set; }
    public string? NotificationResult { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public sealed class ConditionalNotificationFlow : FlowConfig<NotificationState>
{
    protected override void Configure(IFlowBuilder<NotificationState> flow)
    {
        flow.Name("conditional-notification-flow");

        flow.Send<NotificationState, MarkReadyCommand, bool>(s => new MarkReadyCommand())
            .Into((state, result) => state.IsReady = result);

        flow.Send<NotificationState, SendNotificationCommand, string>(s => new SendNotificationCommand())
            .Into((state, result) => state.NotificationResult = result)
            .OnlyWhen(s => s.ShouldNotify);
    }
}

public record MarkReadyCommand : IRequest<bool>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record SendNotificationCommand : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class RemoteCheckoutState : IFlowState
{
    public string? FlowId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool InStock { get; set; }
    public int AvailableQuantity { get; set; }
    public string? ReservationId { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public sealed class RemoteCheckoutFlow : FlowConfig<RemoteCheckoutState>
{
    protected override void Configure(IFlowBuilder<RemoteCheckoutState> flow)
    {
        flow.Name("remote-checkout-flow");

        flow.RemoteSend<RemoteCheckoutState, CheckRemoteStockCommand, RemoteStockResult>(s => new CheckRemoteStockCommand(s.Sku))
            .Into((state, result) =>
            {
                state.InStock = result.Available;
                state.AvailableQuantity = result.Quantity;
            });

        flow.RemoteSend<RemoteCheckoutState, ReserveRemoteStockCommand, ReservationResult>(s => new ReserveRemoteStockCommand(s.Sku, s.Quantity))
            .Into((state, result) => state.ReservationId = result.ReservationId)
            .OnlyWhen(s => s.InStock);
    }
}

public record CheckRemoteStockCommand(string Sku) : IRequest<RemoteStockResult>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record RemoteStockResult(string Sku, int Quantity, bool Available);

public record ReserveRemoteStockCommand(string Sku, int Quantity) : IRequest<ReservationResult>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ReservationResult(string ReservationId);
