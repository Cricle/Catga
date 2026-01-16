using Catga.Flow.Dsl;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Flows;
using OrderSystem.Models;

namespace OrderSystem.Extensions;

public static class FlowEndpointExtensions
{
    public static void MapFlowEndpoints(this WebApplication app)
    {
        var flows = app.MapGroup("/api/flows").WithTags("Flows");

        // Start order fulfillment flow
        flows.MapPost("/fulfillment/start", async (
            [FromBody] StartFulfillmentRequest request,
            [FromServices] IFlowExecutor executor) =>
        {
            var state = new OrderFulfillmentState
            {
                CustomerId = request.CustomerId,
                Items = request.Items
            };

            var result = await executor.ExecuteAsync<OrderFulfillmentFlow, OrderFulfillmentState>(state);

            return result.IsSuccess
                ? Results.Ok(new
                {
                    flowId = result.State?.FlowId,
                    orderId = result.State?.OrderId,
                    status = result.Status.ToString(),
                    total = result.State?.Total ?? 0,
                    isCompleted = result.Status == DslFlowStatus.Completed
                })
                : Results.BadRequest(new { error = result.Error, status = result.Status.ToString() });
        })
        .WithName("StartFulfillmentFlow")
        .WithSummary("Start order fulfillment flow");

        // Start complex order flow
        flows.MapPost("/complex/start", async (
            [FromBody] StartComplexOrderRequest request,
            [FromServices] IFlowExecutor executor) =>
        {
            var state = new ComplexOrderState
            {
                CustomerId = request.CustomerId,
                Items = request.Items,
                Type = request.Type
            };

            var result = await executor.ExecuteAsync<ComplexOrderFlow, ComplexOrderState>(state);

            return result.IsSuccess
                ? Results.Ok(new
                {
                    flowId = result.State?.FlowId,
                    orderId = result.State?.OrderId,
                    status = result.Status.ToString(),
                    total = result.State?.Total ?? 0,
                    processedItems = result.State?.ProcessedItems ?? 0,
                    isCompleted = result.Status == DslFlowStatus.Completed
                })
                : Results.BadRequest(new { error = result.Error, status = result.Status.ToString() });
        })
        .WithName("StartComplexFlow")
        .WithSummary("Start complex order flow with parallel execution");

        // Resume a flow
        flows.MapPost("/resume/{flowId}", async (
            string flowId,
            [FromServices] IFlowExecutor executor) =>
        {
            // Try both flow types
            var fulfillmentResult = await executor.ResumeAsync<OrderFulfillmentFlow, OrderFulfillmentState>(flowId);
            if (fulfillmentResult.IsSuccess || fulfillmentResult.Status != DslFlowStatus.Failed)
            {
                return Results.Ok(new
                {
                    flowId,
                    status = fulfillmentResult.Status.ToString(),
                    orderId = fulfillmentResult.State?.OrderId,
                    error = fulfillmentResult.Error
                });
            }

            var complexResult = await executor.ResumeAsync<ComplexOrderFlow, ComplexOrderState>(flowId);
            return Results.Ok(new
            {
                flowId,
                status = complexResult.Status.ToString(),
                orderId = complexResult.State?.OrderId,
                error = complexResult.Error
            });
        })
        .WithName("ResumeFlow")
        .WithSummary("Resume a suspended or failed flow");

        // Get flow status
        flows.MapGet("/status/{flowId}", async (
            string flowId,
            [FromServices] IFlowExecutor executor) =>
        {
            // Try fulfillment flow first
            var snapshot = await executor.GetSnapshotAsync<OrderFulfillmentState>(flowId);
            if (snapshot != null)
            {
                return Results.Ok(new
                {
                    flowId = snapshot.FlowId,
                    status = snapshot.Status.ToString(),
                    orderId = snapshot.State.OrderId,
                    total = snapshot.State.Total,
                    isValidated = snapshot.State.IsValidated,
                    isPaymentProcessed = snapshot.State.IsPaymentProcessed,
                    isShipped = snapshot.State.IsShipped,
                    trackingNumber = snapshot.State.TrackingNumber,
                    createdAt = snapshot.CreatedAt,
                    updatedAt = snapshot.UpdatedAt,
                    version = snapshot.Version
                });
            }

            // Try complex flow
            var complexSnapshot = await executor.GetSnapshotAsync<ComplexOrderState>(flowId);
            if (complexSnapshot != null)
            {
                return Results.Ok(new
                {
                    flowId = complexSnapshot.FlowId,
                    status = complexSnapshot.Status.ToString(),
                    orderId = complexSnapshot.State.OrderId,
                    total = complexSnapshot.State.Total,
                    type = complexSnapshot.State.Type.ToString(),
                    processedItems = complexSnapshot.State.ProcessedItems,
                    createdAt = complexSnapshot.CreatedAt,
                    updatedAt = complexSnapshot.UpdatedAt,
                    version = complexSnapshot.Version
                });
            }

            return Results.NotFound(new { error = "Flow not found" });
        })
        .WithName("GetFlowStatus")
        .WithSummary("Get flow status and state");

        // Cancel a flow
        flows.MapPost("/cancel/{flowId}", async (
            string flowId,
            [FromServices] IFlowExecutor executor) =>
        {
            var cancelled = await executor.CancelAsync(flowId);
            return cancelled
                ? Results.Ok(new { flowId, cancelled = true })
                : Results.NotFound(new { error = "Flow not found or already completed" });
        })
        .WithName("CancelFlow")
        .WithSummary("Cancel a running flow");
    }
}

public record StartFulfillmentRequest(string CustomerId, List<OrderItem> Items);
public record StartComplexOrderRequest(string CustomerId, List<OrderItem> Items, OrderType Type);
