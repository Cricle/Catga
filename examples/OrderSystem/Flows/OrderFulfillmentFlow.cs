using Catga.Flow.Dsl;
using OrderSystem.Commands;
using OrderSystem.Events;
using OrderSystem.Queries;

namespace OrderSystem.Flows;

/// <summary>
/// Order fulfillment flow demonstrating:
/// - Sequential command execution
/// - Conditional branching
/// - Compensation (Saga pattern)
/// - Event publishing
/// - Recovery capability
/// </summary>
public class OrderFulfillmentFlow : FlowConfig<OrderFulfillmentState>
{
    protected override void Configure(IFlowBuilder<OrderFulfillmentState> flow)
    {
        flow.Name("order-fulfillment");

        // Step 1: Create the order
        flow.Send(state => new CreateOrderCommand(state.CustomerId, state.Items))
            .Into((state, result) =>
            {
                state.OrderId = result.OrderId;
                state.Total = result.Total;
            })
            .IfFail(state => new CancelOrderCommand(state.OrderId));

        // Step 2: Validate order (conditional)
        flow.If(state => state.Total > 0)
            .Send(state => new GetOrderQuery(state.OrderId))
            .Into((state, order) => state.IsValidated = order != null)
            .EndIf();

        // Step 3: Process payment with compensation
        flow.Send(state => new PayOrderCommand(state.OrderId, "CreditCard"))
            .IfFail(state => new CancelOrderCommand(state.OrderId));

        // Step 4: Ship order
        flow.Send(state => new ShipOrderCommand(state.OrderId, $"TRACK-{state.OrderId[..8]}"));

        // Step 5: Publish completion event
        flow.Publish(state => new OrderCompletedEvent(state.OrderId, state.Total));
    }
}
