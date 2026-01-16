using Catga.Flow.Dsl;
using OrderSystem.Commands;
using OrderSystem.Events;
using OrderSystem.Queries;

namespace OrderSystem.Flows;

/// <summary>
/// Complex order flow demonstrating:
/// - Parallel execution (WhenAll)
/// - Switch branching
/// - ForEach iteration
/// - Distributed execution
/// </summary>
public class ComplexOrderFlow : FlowConfig<ComplexOrderState>
{
    protected override void Configure(IFlowBuilder<ComplexOrderState> flow)
    {
        flow.Name("complex-order");

        // Step 1: Create order
        flow.Send(state => new CreateOrderCommand(state.CustomerId, state.Items))
            .Into((state, result) =>
            {
                state.OrderId = result.OrderId;
                state.Total = result.Total;
            });

        // Step 2: Switch based on order type
        flow.Switch(state => state.Type)
            .Case(OrderType.Standard, branch =>
            {
                branch.Send(state => new PayOrderCommand(state.OrderId, "Standard"));
            })
            .Case(OrderType.Express, branch =>
            {
                branch.Send(state => new PayOrderCommand(state.OrderId, "Express"));
            })
            .Case(OrderType.Bulk, branch =>
            {
                branch.Send(state => new PayOrderCommand(state.OrderId, "Bulk"));
            })
            .Default(branch =>
            {
                branch.Send(state => new PayOrderCommand(state.OrderId, "Default"));
            });

        // Step 3: Process each item (ForEach)
        flow.ForEach(state => state.Items)
            .Configure((item, builder) =>
            {
                // In a real scenario, this would be a command to process the item
                // For demo, we'll just use GetOrderQuery
                builder.Send(state => new GetOrderQuery(state.OrderId));
            })
            .OnItemSuccess((state, item, result) =>
            {
                state.ProcessedItems++;
            })
            .EndForEach();

        // Step 4: Ship order
        flow.Send(state => new ShipOrderCommand(state.OrderId, $"TRACK-{state.OrderId[..8]}"));

        // Step 5: Publish completion
        flow.Publish(state => new OrderCompletedEvent(state.OrderId, state.Total));
    }
}
