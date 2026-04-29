using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderSystem.Extensions;
using OrderSystem.Flows;
using OrderSystem.Models;
using Xunit;

namespace Catga.E2E.Tests;

[Collection("OrderSystem")]
public class OrderSystemFlowE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemFlowE2ETests(OrderSystemFixture factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StartFulfillmentFlow_CompletesAndShipsOrder()
    {
        var items = new List<OrderItem>
        {
            new("FLOW-CPU", "Flow CPU", 2, 99.50m),
            new("FLOW-SSD", "Flow SSD", 1, 149.99m)
        };

        var started = await StartFulfillmentFlowAsync(items);

        Assert.Equal("Completed", started.Status);
        Assert.True(started.IsCompleted);
        Assert.Equal(348.99m, started.Total);
        Assert.Matches("^[a-f0-9]{8}$", started.OrderId);

        var order = await WaitForOrderAsync(started.OrderId);
        Assert.Equal("Shipped", order.Status);
        Assert.Equal(started.Total, order.Total);
        Assert.Equal($"TRACK-{started.OrderId}", order.TrackingNumber);
        Assert.NotNull(order.PaidAt);
        Assert.NotNull(order.ShippedAt);

        var status = await WaitForFlowStatusAsync(started.FlowId);
        Assert.Equal("Completed", status.Status);
        Assert.Equal(started.OrderId, status.OrderId);
        Assert.Equal(started.Total, status.Total);
        Assert.True(status.IsValidated);
        Assert.True(status.Version >= 1);

        var history = await GetOrderHistoryAsync(started.OrderId);
        Assert.True(history.Count >= 3);
    }

    [Fact]
    public async Task StartFulfillmentFlow_WithZeroTotal_SkipsValidationButStillCompletes()
    {
        var items = new List<OrderItem>
        {
            new("FREE-GIFT", "Free Gift", 1, 0m)
        };

        var started = await StartFulfillmentFlowAsync(items);

        Assert.Equal("Completed", started.Status);
        Assert.True(started.IsCompleted);
        Assert.Equal(0m, started.Total);

        var order = await WaitForOrderAsync(started.OrderId);
        Assert.Equal("Shipped", order.Status);
        Assert.Equal(0m, order.Total);

        var status = await WaitForFlowStatusAsync(started.FlowId);
        Assert.Equal("Completed", status.Status);
        Assert.False(status.IsValidated);
        Assert.Equal(0m, status.Total);
    }

    [Fact]
    public async Task StartComplexFlow_CompletesAndTracksProcessedItems()
    {
        var items = new List<OrderItem>
        {
            new("EXP-1", "Express Item 1", 1, 25m),
            new("EXP-2", "Express Item 2", 2, 10m),
            new("EXP-3", "Express Item 3", 3, 5m)
        };

        var response = await _client.PostAsJsonAsync(
            "/api/flows/complex/start",
            new StartComplexOrderRequest(
                $"flow-complex-{Guid.NewGuid():N}",
                items,
                OrderType.Express));

        await AssertStatusCodeAsync(response, HttpStatusCode.OK);

        var started = await response.Content.ReadFromJsonAsync<FlowStartResponse>(_jsonOptions);
        Assert.NotNull(started);
        Assert.Equal("Completed", started.Status);
        Assert.True(started.IsCompleted);
        Assert.Equal(60m, started.Total);
        Assert.Equal(items.Count, started.ProcessedItems);

        var order = await WaitForOrderAsync(started.OrderId);
        Assert.Equal("Shipped", order.Status);
        Assert.Equal(started.Total, order.Total);

        var status = await WaitForFlowStatusAsync(started.FlowId);
        Assert.Equal("Completed", status.Status);
        Assert.Equal("Express", status.Type);
        Assert.Equal(items.Count, status.ProcessedItems);
        Assert.Equal(started.OrderId, status.OrderId);
    }

    [Fact]
    public async Task ResumeCompletedFlow_ReturnsCompletedAndSameOrderId()
    {
        var started = await StartFulfillmentFlowAsync(
        [
            new OrderItem("RESUME-1", "Resume Item", 1, 42.5m)
        ]);

        var response = await _client.PostAsync($"/api/flows/resume/{started.FlowId}", null);

        await AssertStatusCodeAsync(response, HttpStatusCode.OK);

        var resumed = await response.Content.ReadFromJsonAsync<FlowResumeResponse>(_jsonOptions);
        Assert.NotNull(resumed);
        Assert.Equal(started.FlowId, resumed.FlowId);
        Assert.Equal("Completed", resumed.Status);
        Assert.Equal(started.OrderId, resumed.OrderId);
        Assert.True(string.IsNullOrEmpty(resumed.Error));
    }

    [Fact]
    public async Task GetFlowStatus_UnknownFlow_ReturnsNotFound()
    {
        var flowId = Guid.NewGuid().ToString("N");

        var response = await _client.GetAsync($"/api/flows/status/{flowId}");

        await AssertStatusCodeAsync(response, HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(_jsonOptions);
        Assert.NotNull(error);
        Assert.Equal("Flow not found", error.Error);
    }

    [Fact]
    public async Task CancelCompletedFlow_ReturnsNotFound()
    {
        var started = await StartFulfillmentFlowAsync(
        [
            new OrderItem("CANCEL-1", "Cancel Item", 1, 10m)
        ]);

        var response = await _client.PostAsync($"/api/flows/cancel/{started.FlowId}", null);

        await AssertStatusCodeAsync(response, HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(_jsonOptions);
        Assert.NotNull(error);
        Assert.Contains("already completed", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<FlowStartResponse> StartFulfillmentFlowAsync(List<OrderItem> items)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/flows/fulfillment/start",
            new StartFulfillmentRequest(
                $"flow-fulfillment-{Guid.NewGuid():N}",
                items));

        await AssertStatusCodeAsync(response, HttpStatusCode.OK);

        var started = await response.Content.ReadFromJsonAsync<FlowStartResponse>(_jsonOptions);
        Assert.NotNull(started);
        Assert.False(string.IsNullOrWhiteSpace(started.FlowId));

        return started;
    }

    private async Task<OrderResponse> WaitForOrderAsync(string orderId)
    {
        HttpStatusCode lastStatusCode = HttpStatusCode.NotFound;
        string? lastContent = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await _client.GetAsync($"/orders/{orderId}");
            lastStatusCode = response.StatusCode;
            lastContent = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var order = JsonSerializer.Deserialize<OrderResponse>(lastContent, _jsonOptions);
                if (order != null)
                    return order;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException(
            $"Order {orderId} was not available after retries. Last status: {lastStatusCode}, body: {lastContent}");
    }

    private async Task<FlowStatusResponse> WaitForFlowStatusAsync(string flowId)
    {
        HttpStatusCode lastStatusCode = HttpStatusCode.NotFound;
        string? lastContent = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await _client.GetAsync($"/api/flows/status/{flowId}");
            lastStatusCode = response.StatusCode;
            lastContent = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var status = JsonSerializer.Deserialize<FlowStatusResponse>(lastContent, _jsonOptions);
                if (status != null)
                    return status;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException(
            $"Flow {flowId} status was not available after retries. Last status: {lastStatusCode}, body: {lastContent}");
    }

    private async Task<List<object>> GetOrderHistoryAsync(string orderId)
    {
        var response = await _client.GetAsync($"/orders/{orderId}/history");
        await AssertStatusCodeAsync(response, HttpStatusCode.OK);

        var history = await response.Content.ReadFromJsonAsync<List<object>>(_jsonOptions);
        Assert.NotNull(history);
        return history;
    }

    private static async Task AssertStatusCodeAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        if (response.StatusCode == expectedStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        throw new Xunit.Sdk.XunitException(
            $"Expected status {(int)expectedStatusCode} ({expectedStatusCode}) but got {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
    }

    private sealed record FlowStartResponse(
        string FlowId,
        string OrderId,
        string Status,
        decimal Total,
        bool IsCompleted,
        int ProcessedItems = 0);

    private sealed record FlowStatusResponse(
        string FlowId,
        string Status,
        string OrderId,
        decimal Total,
        bool IsValidated,
        int ProcessedItems,
        string? Type,
        int Version);

    private sealed record FlowResumeResponse(
        string FlowId,
        string Status,
        string? OrderId,
        string? Error);

    private sealed record OrderResponse(
        string Id,
        string CustomerId,
        decimal Total,
        string Status,
        DateTime CreatedAt,
        DateTime? PaidAt,
        DateTime? ShippedAt,
        string? TrackingNumber)
    {
        public string OrderId => Id;
    }

    private sealed record ApiErrorResponse(string Error);
}
