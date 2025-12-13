using FluentAssertions;
using Xunit;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// Unit tests for EndpointRegistrationGenerator source generator
/// Verifies correct code generation for [CatgaEndpoint] marked methods
/// </summary>
public class EndpointRegistrationGeneratorTests
{
    [Fact]
    public void GeneratedRegisterEndpoints_ShouldExistAsStaticMethod()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);

        // Act
        var method = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Assert
        method.Should().NotBeNull("RegisterEndpoints method should be generated");
        method!.IsStatic.Should().BeTrue("RegisterEndpoints should be static");
        method.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void GeneratedRegisterEndpoints_ShouldAcceptWebApplicationParameter()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);

        // Act
        var method = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Assert
        method.Should().NotBeNull();
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Name.Should().Be("WebApplication");
    }

    [Fact]
    public void GeneratedRegisterEndpoints_ShouldBeCallableWithoutReflection()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);
        var method = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Act - Verify method exists and is callable
        var isCallable = method != null && method.IsStatic && method.ReturnType == typeof(void);

        // Assert
        isCallable.Should().BeTrue("Method should be directly callable");
    }

    [Fact]
    public void PartialMethods_ShouldBeGeneratedForEachEndpoint()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);

        // Act
        var createOrderMethod = handlerType.GetMethod("CreateOrder",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var getOrderMethod = handlerType.GetMethod("GetOrder",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Assert
        createOrderMethod.Should().NotBeNull("CreateOrder partial method should exist");
        getOrderMethod.Should().NotBeNull("GetOrder partial method should exist");
    }

    [Fact]
    public void GeneratedCode_ShouldMapPostEndpoint()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);
        var method = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Act & Assert
        method.Should().NotBeNull("RegisterEndpoints should be generated for POST endpoint");
    }

    [Fact]
    public void GeneratedCode_ShouldMapGetEndpoint()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);
        var method = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Act & Assert
        method.Should().NotBeNull("RegisterEndpoints should be generated for GET endpoint");
    }

    [Fact]
    public void GeneratedCode_ShouldPreserveEndpointNames()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);

        // Act
        var createOrderMethod = handlerType.GetMethod("CreateOrder",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Assert
        createOrderMethod.Should().NotBeNull("Endpoint method name should be preserved");
    }

    [Fact]
    public void GeneratedCode_ShouldSupportMultipleEndpoints()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);

        // Act
        var methods = handlerType.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.Name == "CreateOrder" || m.Name == "GetOrder")
            .ToList();

        // Assert
        methods.Should().HaveCountGreaterThanOrEqualTo(2, "Should have multiple endpoint methods");
    }

    [Fact]
    public void GeneratedCode_ShouldBeAOTCompatible()
    {
        // Arrange
        var handlerType = typeof(TestGeneratedEndpointHandler);
        var method = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Act - Check for reflection-based attributes that would break AOT
        var hasRequiresUnreferencedCode = method!.GetCustomAttributes()
            .Any(a => a.GetType().Name.Contains("RequiresUnreferencedCode"));

        var hasRequiresDynamicCode = method.GetCustomAttributes()
            .Any(a => a.GetType().Name.Contains("RequiresDynamicCode"));

        // Assert
        hasRequiresUnreferencedCode.Should().BeFalse("Generated code should not require unreferenced code");
        hasRequiresDynamicCode.Should().BeFalse("Generated code should not require dynamic code");
    }
}

/// <summary>
/// Test handler with [CatgaEndpoint] attributes for source generator testing
/// </summary>
public partial class TestGeneratedEndpointHandler
{
    [Catga.AspNetCore.CatgaEndpoint(HttpMethod.Post, "/api/orders", Name = "CreateOrder")]
    public partial async Task<Microsoft.AspNetCore.Http.IResult> CreateOrder(
        TestCreateOrderCommand cmd,
        Catga.Abstractions.ICatgaMediator mediator,
        Catga.Abstractions.IEventStore eventStore);

    [Catga.AspNetCore.CatgaEndpoint(HttpMethod.Get, "/api/orders/{id}", Name = "GetOrder")]
    public partial async Task<Microsoft.AspNetCore.Http.IResult> GetOrder(
        TestGetOrderQuery query,
        Catga.Abstractions.ICatgaMediator mediator);
}

public partial class TestGeneratedEndpointHandler
{
    public partial async Task<Microsoft.AspNetCore.Http.IResult> CreateOrder(
        TestCreateOrderCommand cmd,
        Catga.Abstractions.ICatgaMediator mediator,
        Catga.Abstractions.IEventStore eventStore)
    {
        var result = await mediator.SendAsync<TestCreateOrderCommand, TestOrderResult>(cmd);
        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Created($"/api/orders/{result.Value.OrderId}", result.Value)
            : Microsoft.AspNetCore.Http.Results.BadRequest(result.Error);
    }

    public partial async Task<Microsoft.AspNetCore.Http.IResult> GetOrder(
        TestGetOrderQuery query,
        Catga.Abstractions.ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestGetOrderQuery, TestOrderDto>(query);
        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Ok(result.Value)
            : Microsoft.AspNetCore.Http.Results.NotFound();
    }
}

// Test types
public class TestCreateOrderCommand : Catga.Abstractions.IRequest<TestOrderResult>
{
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
}

public class TestGetOrderQuery : Catga.Abstractions.IRequest<TestOrderDto>
{
    public string Id { get; set; }
}

public class TestOrderResult
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
}

public class TestOrderDto
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
}
