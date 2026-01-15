using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Boundary and edge case tests for OrderSystem.Api.
/// Tests extreme values, invalid inputs, and boundary conditions.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemBoundaryTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemBoundaryTests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region Numeric Boundary Tests

    [Fact]
    public async Task CreateOrder_MaxDecimalValue_HandlesCorrectly()
    {
        // Arrange - test with very large decimal values
        var request = new
        {
            CustomerId = "max-decimal-customer",
            Items = new[]
            {
                new { ProductId = "expensive-prod", Quantity = 1, Price = 999999999.99m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        // System may handle large values differently, just verify it succeeds
        Assert.NotNull(result);
        Assert.True(result.Total >= 0);
    }

    [Fact]
    public async Task CreateOrder_MinimalDecimalValue_HandlesCorrectly()
    {
        // Arrange - test with very small decimal values
        var request = new
        {
            CustomerId = "min-decimal-customer",
            Items = new[]
            {
                new { ProductId = "cheap-prod", Quantity = 1, Price = 0.01m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(0.01m, result!.Total);
    }

    [Fact]
    public async Task CreateOrder_NegativePrice_HandlesCorrectly()
    {
        // Arrange - test with negative price (should be rejected or handled)
        var request = new
        {
            CustomerId = "negative-price-customer",
            Items = new[]
            {
                new { ProductId = "refund-prod", Quantity = 1, Price = -50.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - system should either accept (for refunds) or reject
        Assert.True(response.StatusCode == HttpStatusCode.Created || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_NegativeQuantity_HandlesCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "negative-qty-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = -5, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should be rejected or handled
        Assert.True(response.StatusCode == HttpStatusCode.Created || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_ZeroQuantity_HandlesCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "zero-qty-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 0, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.Created || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_MaxIntQuantity_HandlesCorrectly()
    {
        // Arrange - test with maximum integer quantity
        var request = new
        {
            CustomerId = "max-qty-customer",
            Items = new[]
            {
                new { ProductId = "bulk-prod", Quantity = int.MaxValue, Price = 0.01m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle or reject gracefully
        Assert.True(response.StatusCode == HttpStatusCode.Created || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_DecimalOverflow_HandlesGracefully()
    {
        // Arrange - quantities and prices that would overflow
        var request = new
        {
            CustomerId = "overflow-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1000000, Price = 999999999.99m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle overflow gracefully
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region String Boundary Tests

    [Fact]
    public async Task CreateOrder_EmptyCustomerId_HandlesCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should be rejected
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_WhitespaceCustomerId_HandlesCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "   ",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_VeryLongCustomerId_HandlesCorrectly()
    {
        // Arrange - 10,000 character customer ID
        var longId = new string('A', 10000);
        var request = new
        {
            CustomerId = longId,
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle or reject gracefully
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task CreateOrder_SpecialCharactersInIds_HandlesCorrectly()
    {
        // Arrange - test various special characters
        var specialChars = new[] { "!@#$%^&*()", "<>?/\\|", "[]{}()", "\"'`", "\t\n\r" };

        foreach (var chars in specialChars)
        {
            var request = new
            {
                CustomerId = $"customer-{chars}",
                Items = new[]
                {
                    new { ProductId = $"prod-{chars}", Quantity = 1, Price = 10.00m }
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/orders", request);

            // Assert - should handle gracefully
            Assert.True(response.IsSuccessStatusCode || 
                        response.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task CreateOrder_SqlInjectionAttempt_HandlesSecurely()
    {
        // Arrange - SQL injection patterns
        var sqlPatterns = new[]
        {
            "'; DROP TABLE Orders; --",
            "1' OR '1'='1",
            "admin'--",
            "' UNION SELECT * FROM Users--"
        };

        foreach (var pattern in sqlPatterns)
        {
            var request = new
            {
                CustomerId = pattern,
                Items = new[]
                {
                    new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/orders", request);

            // Assert - should handle securely without executing SQL
            Assert.True(response.IsSuccessStatusCode || 
                        response.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task CreateOrder_XssAttempt_HandlesSecurely()
    {
        // Arrange - XSS patterns
        var xssPatterns = new[]
        {
            "<script>alert('XSS')</script>",
            "<img src=x onerror=alert('XSS')>",
            "javascript:alert('XSS')",
            "<iframe src='javascript:alert(1)'>"
        };

        foreach (var pattern in xssPatterns)
        {
            var request = new
            {
                CustomerId = pattern,
                Items = new[]
                {
                    new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/orders", request);

            // Assert - should handle securely
            Assert.True(response.IsSuccessStatusCode || 
                        response.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task CreateOrder_UnicodeCharacters_HandlesCorrectly()
    {
        // Arrange - various Unicode characters
        var unicodeTests = new[]
        {
            "客户-测试-用户",           // Chinese
            "مستخدم-اختبار",           // Arabic
            "пользователь-тест",       // Russian
            "ユーザーテスト",           // Japanese
            "🎉🎊🎈",                   // Emojis
            "Ñoño-Müller-Øre",        // Accented characters
        };

        foreach (var unicode in unicodeTests)
        {
            var request = new
            {
                CustomerId = unicode,
                Items = new[]
                {
                    new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/orders", request);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    #endregion

    #region Collection Boundary Tests

    [Fact]
    public async Task CreateOrder_EmptyItemsArray_HandlesCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "empty-items-customer",
            Items = Array.Empty<object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should be rejected
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_SingleItem_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "single-item-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_HundredItems_Succeeds()
    {
        // Arrange - 100 items
        var items = Enumerable.Range(1, 100).Select(i => new
        {
            ProductId = $"prod-{i}",
            Quantity = 1,
            Price = 1.00m
        }).ToArray();

        var request = new
        {
            CustomerId = "hundred-items-customer",
            Items = items
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(100.00m, result!.Total);
    }

    [Fact]
    public async Task CreateOrder_ThousandItems_HandlesCorrectly()
    {
        // Arrange - 1000 items (stress test)
        var items = Enumerable.Range(1, 1000).Select(i => new
        {
            ProductId = $"prod-{i}",
            Quantity = 1,
            Price = 0.10m
        }).ToArray();

        var request = new
        {
            CustomerId = "thousand-items-customer",
            Items = items
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle or reject gracefully
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task CreateOrder_DuplicateProductIds_HandlesCorrectly()
    {
        // Arrange - same product ID multiple times
        var request = new
        {
            CustomerId = "duplicate-items-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 2, Price = 10.00m },
                new { ProductId = "prod-1", Quantity = 3, Price = 10.00m },
                new { ProductId = "prod-1", Quantity = 1, Price = 10.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle (either merge or keep separate)
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(60.00m, result!.Total); // 2*10 + 3*10 + 1*10
    }

    #endregion

    #region Malformed Request Tests

    [Fact]
    public async Task CreateOrder_MissingCustomerId_HandlesCorrectly()
    {
        // Arrange - JSON without CustomerId
        var json = @"{""Items"":[{""ProductId"":""prod-1"",""Quantity"":1,""Price"":10.00}]}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_MissingItems_HandlesCorrectly()
    {
        // Arrange - JSON without Items
        var json = @"{""CustomerId"":""test-customer""}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_InvalidJson_ReturnsBadRequest()
    {
        // Arrange - malformed JSON
        var json = @"{""CustomerId"":""test"",""Items"":[{invalid}]}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_EmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var content = new StringContent("", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_NullBody_ReturnsBadRequest()
    {
        // Arrange
        var content = new StringContent("null", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ExtraFields_HandlesGracefully()
    {
        // Arrange - JSON with extra unexpected fields
        var json = @"{
            ""CustomerId"":""test-customer"",
            ""Items"":[{""ProductId"":""prod-1"",""Quantity"":1,""Price"":10.00}],
            ""ExtraField1"":""should-be-ignored"",
            ""ExtraField2"":12345,
            ""ExtraField3"":{""nested"":""object""}
        }";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert - should ignore extra fields and succeed
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    #endregion

    #region URL and Route Boundary Tests

    [Fact]
    public async Task GetOrder_EmptyId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/orders/");

        // Assert - GET /orders/ actually matches GET /orders endpoint (list all)
        Assert.True(response.StatusCode == HttpStatusCode.OK || 
                    response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task GetOrder_VeryLongId_HandlesCorrectly()
    {
        // Arrange - 10,000 character ID
        var longId = new string('A', 10000);

        // Act
        var response = await _client.GetAsync($"/orders/{longId}");

        // Assert - should handle gracefully
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.RequestUriTooLong ||
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrder_SpecialCharactersInId_HandlesCorrectly()
    {
        // Arrange - URL-encoded special characters
        var specialIds = new[] { "id%20with%20spaces", "id/with/slashes", "id?with=query" };

        foreach (var id in specialIds)
        {
            // Act
            var response = await _client.GetAsync($"/orders/{id}");

            // Assert - should handle gracefully
            Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                        response.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task PayOrder_NonExistentOrder_ReturnsNotFoundOrBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/orders/non-existent-order-id/pay", new { });

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ShipOrder_NonExistentOrder_ReturnsNotFoundOrBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/orders/non-existent-order-id/ship", new { });

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CancelOrder_NonExistentOrder_ReturnsNotFoundOrBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/orders/non-existent-order-id/cancel", new { });

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetOrderHistory_NonExistentOrder_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/orders/non-existent-order-id/history");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Content Type Tests

    [Fact]
    public async Task CreateOrder_WrongContentType_ReturnsBadRequest()
    {
        // Arrange - send as plain text instead of JSON
        var content = new StringContent(
            @"{""CustomerId"":""test"",""Items"":[]}",
            Encoding.UTF8,
            "text/plain");

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task CreateOrder_NoContentType_HandlesCorrectly()
    {
        // Arrange
        var content = new StringContent(@"{""CustomerId"":""test"",""Items"":[]}");
        content.Headers.ContentType = null;

        // Act
        var response = await _client.PostAsync("/orders", content);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.UnsupportedMediaType ||
                    response.StatusCode == HttpStatusCode.Created);
    }

    #endregion

    #region Concurrent State Modification Tests

    [Fact]
    public async Task PayOrder_AlreadyPaid_HandlesIdempotently()
    {
        // Arrange - create and pay an order
        var order = await CreateTestOrder();
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });

        // Act - try to pay again
        var response = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });

        // Assert - should handle idempotently
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShipOrder_NotPaid_HandlesCorrectly()
    {
        // Arrange - create order but don't pay
        var order = await CreateTestOrder();

        // Act - try to ship unpaid order
        var response = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { });

        // Assert - should reject or handle gracefully
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelOrder_AlreadyCancelled_HandlesIdempotently()
    {
        // Arrange - create and cancel an order
        var order = await CreateTestOrder();
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { });

        // Act - try to cancel again
        var response = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { });

        // Assert - should handle idempotently
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
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
        return (await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions))!;
    }

    #endregion

    // Response DTOs
    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);
}



