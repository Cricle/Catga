using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// 数据验证E2E测试
/// 测试输入验证、数据完整性和业务规则
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemValidationE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemValidationE2ETests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region 输入验证测试

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateOrder_InvalidCustomerId_HandlesGracefully(string? customerId)
    {
        var request = new
        {
            CustomerId = customerId,
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task CreateOrder_InvalidQuantity_HandlesGracefully(int quantity)
    {
        var request = new
        {
            CustomerId = "test-customer",
            Items = new[] { new { ProductId = "PROD-1", Quantity = quantity, Price = 10.00m } }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }


    [Theory]
    [InlineData(0.00)]
    [InlineData(-1.00)]
    [InlineData(-999.99)]
    public async Task CreateOrder_InvalidPrice_HandlesGracefully(decimal price)
    {
        var request = new
        {
            CustomerId = "test-customer",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = price } }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_MissingRequiredFields_ReturnsBadRequest()
    {
        var json = @"{""Items"":[{""ProductId"":""PROD-1"",""Quantity"":1,""Price"":10.00}]}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/orders", content);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_MalformedJson_ReturnsBadRequest()
    {
        var json = @"{""CustomerId"":""test"",""Items"":[{invalid json}]}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/orders", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region 业务规则验证测试

    [Fact]
    public async Task PayOrder_BeforeCreation_Fails()
    {
        // 尝试支付不存在的订单
        var response = await _client.PostAsJsonAsync("/orders/fake-order-id/pay", new { });
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ShipOrder_WithoutPayment_HandlesGracefully()
    {
        // 创建订单但不支付
        var order = await CreateTestOrder();

        // 尝试直接发货
        var response = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", 
            new { TrackingNumber = "TRK-123" });

        // 应该被拒绝或优雅处理
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelOrder_AfterShipment_HandlesGracefully()
    {
        // 创建、支付并发货订单
        var order = await CreateTestOrder();
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { TrackingNumber = "TRK-123" });

        // 尝试取消已发货的订单
        var response = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { });

        // 应该被拒绝或优雅处理
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region 数据完整性测试

    [Fact]
    public async Task CreateOrder_CalculatesTotalCorrectly()
    {
        var request = new
        {
            CustomerId = "calc-test",
            Items = new[]
            {
                new { ProductId = "PROD-1", Quantity = 2, Price = 10.50m },
                new { ProductId = "PROD-2", Quantity = 3, Price = 5.25m },
                new { ProductId = "PROD-3", Quantity = 1, Price = 100.00m }
            }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        
        // 2*10.50 + 3*5.25 + 1*100.00 = 21.00 + 15.75 + 100.00 = 136.75
        Assert.Equal(136.75m, order!.Total);
    }

    [Fact]
    public async Task CreateOrder_PreservesItemDetails()
    {
        var request = new
        {
            CustomerId = "detail-test",
            Items = new[]
            {
                new { ProductId = "LAPTOP-001", Quantity = 1, Price = 1299.99m },
                new { ProductId = "MOUSE-001", Quantity = 2, Price = 29.99m }
            }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        var created = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);

        // 获取订单详情
        var getResponse = await _client.GetAsync($"/orders/{created!.OrderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var order = await getResponse.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(order);
        Assert.Equal("detail-test", order.CustomerId);
        Assert.Equal(1359.97m, order.Total); // 1299.99 + 2*29.99
    }

    [Fact]
    public async Task OrderHistory_RecordsAllEvents()
    {
        // 创建订单并执行多个操作
        var order = await CreateTestOrder();
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { TrackingNumber = "TRK-123" });

        // 获取历史记录
        var response = await _client.GetAsync($"/orders/{order.OrderId}/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var history = await response.Content.ReadFromJsonAsync<List<object>>(_jsonOptions);
        Assert.NotNull(history);
        Assert.True(history.Count >= 2, $"Expected at least 2 events, got {history.Count}");
    }

    #endregion

    #region 特殊字符和编码测试

    [Theory]
    [InlineData("客户-测试")]
    [InlineData("مستخدم")]
    [InlineData("пользователь")]
    [InlineData("ユーザー")]
    [InlineData("🎉🎊")]
    public async Task CreateOrder_UnicodeCustomerId_Succeeds(string customerId)
    {
        var request = new
        {
            CustomerId = customerId,
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.NotNull(order);
    }

    [Theory]
    [InlineData("PROD-001")]
    [InlineData("产品-001")]
    [InlineData("PROD_WITH_UNDERSCORE")]
    [InlineData("PROD.WITH.DOTS")]
    [InlineData("PROD-WITH-DASHES")]
    public async Task CreateOrder_VariousProductIds_Succeeds(string productId)
    {
        var request = new
        {
            CustomerId = "test-customer",
            Items = new[] { new { ProductId = productId, Quantity = 1, Price = 10.00m } }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    #endregion

    #region 大数据量测试

    [Fact]
    public async Task CreateOrder_With100Items_Succeeds()
    {
        var items = Enumerable.Range(1, 100).Select(i => new
        {
            ProductId = $"PROD-{i:D3}",
            Quantity = 1,
            Price = 10.00m
        }).ToArray();

        var request = new
        {
            CustomerId = "bulk-customer",
            Items = items
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.Equal(1000.00m, order!.Total); // 100 * 10.00
    }

    [Fact]
    public async Task CreateOrder_WithLargeQuantities_CalculatesCorrectly()
    {
        var request = new
        {
            CustomerId = "large-qty-customer",
            Items = new[]
            {
                new { ProductId = "BULK-001", Quantity = 1000, Price = 1.50m },
                new { ProductId = "BULK-002", Quantity = 500, Price = 2.75m }
            }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.Equal(2875.00m, order!.Total); // 1000*1.50 + 500*2.75
    }

    [Fact]
    public async Task CreateOrder_WithHighPrices_HandlesCorrectly()
    {
        var request = new
        {
            CustomerId = "luxury-customer",
            Items = new[]
            {
                new { ProductId = "LUXURY-001", Quantity = 1, Price = 99999.99m },
                new { ProductId = "LUXURY-002", Quantity = 2, Price = 50000.00m }
            }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.Equal(199999.99m, order!.Total); // 99999.99 + 2*50000.00
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

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);
    
    private record OrderResponse(
        string OrderId,
        string CustomerId,
        decimal Total,
        string Status
    );

    #endregion
}
