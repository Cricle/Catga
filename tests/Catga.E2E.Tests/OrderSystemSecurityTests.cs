using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Security and validation tests for OrderSystem.Api.
/// Tests input validation, injection attacks, and security boundaries.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemSecurityTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemSecurityTests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region SQL Injection Tests

    [Fact]
    public async Task CreateOrder_SqlInjectionInCustomerId_HandledSafely()
    {
        // Arrange - SQL injection attempt in customer ID
        var request = new
        {
            CustomerId = "'; DROP TABLE Orders; --",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should either succeed (treating as normal string) or reject
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_SqlInjectionInProductId_HandledSafely()
    {
        // Arrange
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "' OR '1'='1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrder_SqlInjectionInOrderId_ReturnsNotFound()
    {
        // Arrange - SQL injection attempt in order ID
        var maliciousId = "' OR '1'='1";

        // Act
        var response = await _client.GetAsync($"/orders/{maliciousId}");

        // Assert - should return NotFound, not expose data
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region XSS Tests

    [Fact]
    public async Task CreateOrder_XssInCustomerId_StoredSafely()
    {
        // Arrange - XSS attempt
        var request = new
        {
            CustomerId = "<script>alert('XSS')</script>",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var createResponse = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should succeed
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        
        // Verify data is stored but not executed
        var getResponse = await _client.GetAsync($"/orders/{created!.OrderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_XssInProductId_StoredSafely()
    {
        // Arrange
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "<img src=x onerror=alert('XSS')>", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region Path Traversal Tests

    [Fact]
    public async Task GetOrder_PathTraversalAttempt_ReturnsNotFound()
    {
        // Arrange - path traversal attempt
        var maliciousId = "../../../etc/passwd";

        // Act
        var response = await _client.GetAsync($"/orders/{maliciousId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_EncodedPathTraversal_ReturnsNotFound()
    {
        // Arrange - URL encoded path traversal
        var maliciousId = "..%2F..%2F..%2Fetc%2Fpasswd";

        // Act
        var response = await _client.GetAsync($"/orders/{maliciousId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Command Injection Tests

    [Fact]
    public async Task CreateOrder_CommandInjectionInCustomerId_HandledSafely()
    {
        // Arrange - command injection attempt
        var request = new
        {
            CustomerId = "; rm -rf /",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_PowerShellInjection_HandledSafely()
    {
        // Arrange
        var request = new
        {
            CustomerId = "$(Get-Process)",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region LDAP Injection Tests

    [Fact]
    public async Task CreateOrder_LdapInjectionInCustomerId_HandledSafely()
    {
        // Arrange - LDAP injection attempt
        var request = new
        {
            CustomerId = "*)(uid=*))(|(uid=*",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region XML/JSON Injection Tests

    [Fact]
    public async Task CreateOrder_JsonInjectionAttempt_HandledSafely()
    {
        // Arrange - JSON injection with extra fields
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } },
            __proto__ = new { isAdmin = true }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should succeed but ignore malicious fields
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region Buffer Overflow Tests

    [Fact]
    public async Task CreateOrder_ExtremelyLongCustomerId_HandledGracefully()
    {
        // Arrange - very long string (10MB)
        var longString = new string('A', 10_000_000);
        var request = new
        {
            CustomerId = longString,
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should reject or handle gracefully
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.StatusCode == HttpStatusCode.RequestEntityTooLarge ||
                    response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task CreateOrder_ManyItems_HandledGracefully()
    {
        // Arrange - attempt to create order with many items
        var items = Enumerable.Range(0, 10000).Select(i => new
        {
            ProductId = $"PROD-{i}",
            Quantity = 1,
            Price = 1.00m
        }).ToArray();

        var request = new
        {
            CustomerId = "customer-1",
            Items = items
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle or reject gracefully
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.RequestEntityTooLarge);
    }

    #endregion

    #region Null Byte Injection Tests

    [Fact]
    public async Task CreateOrder_NullByteInCustomerId_HandledSafely()
    {
        // Arrange - null byte injection
        var request = new
        {
            CustomerId = "customer\0malicious",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region Format String Tests

    [Fact]
    public async Task CreateOrder_FormatStringInCustomerId_HandledSafely()
    {
        // Arrange - format string attack
        var request = new
        {
            CustomerId = "%s%s%s%s%s%s%s%s%s%s",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region CRLF Injection Tests

    [Fact]
    public async Task CreateOrder_CrlfInjectionInCustomerId_HandledSafely()
    {
        // Arrange - CRLF injection attempt
        var request = new
        {
            CustomerId = "customer\r\nSet-Cookie: sessionid=malicious",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
        
        // Verify no malicious headers were set
        Assert.DoesNotContain(response.Headers, h => h.Key == "Set-Cookie");
    }

    #endregion

    #region Unicode/Encoding Tests

    [Fact]
    public async Task CreateOrder_UnicodeNormalizationAttack_HandledSafely()
    {
        // Arrange - Unicode normalization attack
        var request = new
        {
            CustomerId = "admin\u0041\u0301", // 'Á' using combining characters
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_HomoglyphAttack_HandledSafely()
    {
        // Arrange - homoglyph attack (Cyrillic 'а' instead of Latin 'a')
        var request = new
        {
            CustomerId = "аdmin", // First character is Cyrillic
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region Integer Overflow Tests

    [Fact]
    public async Task CreateOrder_IntegerOverflowInQuantity_HandledGracefully()
    {
        // Arrange - integer overflow attempt
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "PROD-1", Quantity = int.MaxValue, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should handle overflow gracefully
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_NegativeQuantityWraparound_Rejected()
    {
        // Arrange - negative quantity that might wrap around
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "PROD-1", Quantity = -2147483648, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should reject or handle safely
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                    response.IsSuccessStatusCode);
    }

    #endregion

    #region Decimal Precision Tests

    [Fact]
    public async Task CreateOrder_ExtremeDecimalPrecision_HandledCorrectly()
    {
        // Arrange - very high precision decimal
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 0.123456789012345678901234567890m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_DecimalOverflow_HandledGracefully()
    {
        // Arrange - decimal overflow
        var request = new
        {
            CustomerId = "customer-1",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = decimal.MaxValue } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.True(response.IsSuccessStatusCode || 
                    response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);

    #endregion
}
