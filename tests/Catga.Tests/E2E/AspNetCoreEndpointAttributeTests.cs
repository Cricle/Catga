using Catga.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Unit tests for [CatgaEndpoint] attribute validation
/// </summary>
public class AspNetCoreEndpointAttributeTests
{
    [Fact]
    public void CatgaEndpointAttribute_ShouldStoreHttpMethod()
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute("Post", "/api/orders");

        // Assert
        attr.HttpMethod.Should().Be("Post");
    }

    [Fact]
    public void CatgaEndpointAttribute_ShouldStoreRoute()
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute("Get", "/api/orders/{id}");

        // Assert
        attr.Route.Should().Be("/api/orders/{id}");
    }

    [Fact]
    public void CatgaEndpointAttribute_ShouldStoreOptionalName()
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute("Post", "/api/orders")
        {
            Name = "CreateOrder"
        };

        // Assert
        attr.Name.Should().Be("CreateOrder");
    }

    [Fact]
    public void CatgaEndpointAttribute_ShouldStoreOptionalDescription()
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute("Post", "/api/orders")
        {
            Description = "Create a new order"
        };

        // Assert
        attr.Description.Should().Be("Create a new order");
    }

    [Fact]
    public void CatgaEndpointAttribute_ShouldAllowBothNameAndDescription()
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute("Post", "/api/orders")
        {
            Name = "CreateOrder",
            Description = "Create a new order"
        };

        // Assert
        attr.Name.Should().Be("CreateOrder");
        attr.Description.Should().Be("Create a new order");
    }

    [Theory]
    [InlineData("Post")]
    [InlineData("Get")]
    [InlineData("Put")]
    [InlineData("Delete")]
    [InlineData("Patch")]
    public void CatgaEndpointAttribute_ShouldSupportAllHttpMethods(string httpMethod)
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute(httpMethod, "/api/test");

        // Assert
        attr.HttpMethod.Should().Be(httpMethod);
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/orders/{id}")]
    [InlineData("/api/orders/{id}/items/{itemId}")]
    [InlineData("/api/v1/orders")]
    public void CatgaEndpointAttribute_ShouldSupportVariousRoutePatterns(string route)
    {
        // Arrange & Act
        var attr = new CatgaEndpointAttribute("Get", route);

        // Assert
        attr.Route.Should().Be(route);
    }

    [Fact]
    public void CatgaEndpointAttribute_ShouldBeApplicableToMethods()
    {
        // Arrange & Act
        var attributeUsage = typeof(CatgaEndpointAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;

        // Assert
        attributeUsage.Should().NotBeNull();
        attributeUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }
}
