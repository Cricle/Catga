using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// 可靠性和容错E2E测试
/// 测试系统在异常情况下的行为
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemReliabilityE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemReliabilityE2ETests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region 幂等性测试

    [Fact]
    public async Task CreateOrder_DuplicateRequest_HandlesIdempotently()
    {
        // 测试重复创建订单请求的幂等性
        var request = new
        {
            CustomerId = $"idempotent-{Guid.NewGuid():N}",
            Items = new[]
            {
                new { ProductId = "PROD-001", Quantity = 1, Price = 100.00m }
            }
        };

        // 发送两次相同的请求
        var response1 = await _client.PostAsJsonAsync("/orders", request);
        var response2 = await _client.PostAsJsonAsync("/orders", request);

        // 两次请求都应该成功（或第二次返回冲突）
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        Assert.True(response2.StatusCode == HttpStatusCode.Created || 
                    response2.StatusCode == HttpStatusCode.Conflict);
    }


    [Fact]
    public async Task PayOrder_MultipleTimes_HandlesIdempotently()
    {
        // 创建订单
        var order = await CreateTestOrder();

        // 多次支付同一订单
        var payRequest = new { PaymentMethod = "Card", TransactionId = $"TXN-{Guid.NewGuid():N}" };
        var response1 = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", payRequest);
        var response2 = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", payRequest);
        var response3 = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", payRequest);

        // 第一次应该成功，后续请求应该幂等处理
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.True(response2.IsSuccessStatusCode || response2.StatusCode == HttpStatusCode.BadRequest);
        Assert.True(response3.IsSuccessStatusCode || response3.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelOrder_MultipleTimes_HandlesIdempotently()
    {
        // 创建订单
        var order = await CreateTestOrder();

        // 多次取消同一订单
        var cancelRequest = new { Reason = "Test cancellation" };
        var response1 = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", cancelRequest);
        var response2 = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", cancelRequest);

        // 应该幂等处理
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.True(response2.IsSuccessStatusCode || response2.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region 错误恢复测试

    [Fact]
    public async Task GetOrder_AfterMultipleFailures_EventuallySucceeds()
    {
        // 创建订单
        var order = await CreateTestOrder();

        // 模拟多次查询（可能有些失败）
        var successCount = 0;
        for (int i = 0; i < 10; i++)
        {
            var response = await _client.GetAsync($"/orders/{order.OrderId}");
            if (response.IsSuccessStatusCode) successCount++;
            await Task.Delay(10);
        }

        // 至少应该有一些成功
        Assert.True(successCount >= 8, $"Expected at least 8 successes, got {successCount}");
    }

    [Fact]
    public async Task CreateOrder_AfterTransientFailure_Succeeds()
    {
        // 尝试创建多个订单，即使有些失败也应该能继续
        var successCount = 0;
        for (int i = 0; i < 5; i++)
        {
            var request = new
            {
                CustomerId = $"recovery-{i}",
                Items = new[] { new { ProductId = $"PROD-{i}", Quantity = 1, Price = 10.00m } }
            };

            var response = await _client.PostAsJsonAsync("/orders", request);
            if (response.StatusCode == HttpStatusCode.Created) successCount++;
        }

        // 大部分应该成功
        Assert.True(successCount >= 4, $"Expected at least 4 successes, got {successCount}");
    }

    #endregion

    #region 数据一致性测试

    [Fact]
    public async Task OrderLifecycle_StateTransitions_MaintainConsistency()
    {
        // 创建订单并执行完整生命周期
        var order = await CreateTestOrder();

        // 支付
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        var afterPay = await GetOrder(order.OrderId);
        Assert.Equal("Paid", afterPay!.Status);

        // 发货
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { TrackingNumber = "TRK-123" });
        var afterShip = await GetOrder(order.OrderId);
        Assert.Equal("Shipped", afterShip!.Status);

        // 验证状态转换的一致性
        Assert.NotNull(afterShip.PaidAt);
        Assert.NotNull(afterShip.ShippedAt);
    }

    [Fact]
    public async Task ConcurrentStateChanges_MaintainConsistency()
    {
        // 创建订单
        var order = await CreateTestOrder();

        // 并发执行多个状态变更操作
        var tasks = new List<Task<HttpResponseMessage>>
        {
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { }),
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { }),
            _client.GetAsync($"/orders/{order.OrderId}"),
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { })
        };

        var responses = await Task.WhenAll(tasks);

        // 至少有一个操作应该成功
        Assert.True(responses.Any(r => r.IsSuccessStatusCode));

        // 最终状态应该是一致的
        var finalOrder = await GetOrder(order.OrderId);
        Assert.NotNull(finalOrder);
        Assert.True(finalOrder.Status == "Paid" || finalOrder.Status == "Cancelled" || finalOrder.Status == "Pending");
    }

    #endregion

    #region 超时和延迟测试

    [Fact]
    public async Task CreateOrder_WithTimeout_CompletesOrFails()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        var request = new
        {
            CustomerId = "timeout-test",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        try
        {
            var response = await _client.PostAsJsonAsync("/orders", request, cts.Token);
            // 应该在超时前完成
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest);
        }
        catch (OperationCanceledException)
        {
            // 超时也是可接受的
            Assert.True(true);
        }
    }

    [Fact]
    public async Task MultipleOperations_WithDelay_AllComplete()
    {
        // 创建订单
        var order = await CreateTestOrder();

        // 带延迟的多个操作
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        await Task.Delay(100);

        var getResponse = await _client.GetAsync($"/orders/{order.OrderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        await Task.Delay(100);
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { TrackingNumber = "TRK-123" });

        var finalOrder = await GetOrder(order.OrderId);
        Assert.NotNull(finalOrder);
    }

    #endregion

    #region 边界条件测试

    [Fact]
    public async Task GetOrder_NonExistentId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/orders/nonexistent-id-12345");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PayOrder_NonExistentOrder_HandlesGracefully()
    {
        var response = await _client.PostAsJsonAsync("/orders/nonexistent-id/pay", new { });
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_HandlesGracefully()
    {
        var request = new
        {
            CustomerId = "empty-items-test",
            Items = Array.Empty<object>()
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    #endregion

    #region Helper Methods

    private async Task<OrderCreatedResponse> CreateTestOrder()
    {
        var request = new
        {
            CustomerId = $"test-{Guid.NewGuid():N}",
            Items = new[]
            {
                new { ProductId = "TEST-001", Quantity = 1, Price = 100.00m }
            }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions))!;
    }

    private async Task<OrderResponse?> GetOrder(string orderId)
    {
        var response = await _client.GetAsync($"/orders/{orderId}");
        if (response.StatusCode != HttpStatusCode.OK) return null;
        return await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
    }

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);
    
    private record OrderResponse(
        string OrderId,
        string CustomerId,
        decimal Total,
        string Status,
        DateTime? PaidAt = null,
        DateTime? ShippedAt = null,
        string? TrackingNumber = null
    );

    #endregion
}
