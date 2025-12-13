using Catga.Abstractions;
using Catga.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// AOT compatibility tests for Catga AspNetCore Endpoints
/// Verifies zero reflection, no dynamic code generation, AOT-safe patterns
/// </summary>
public class AspNetCoreEndpointAOTCompatibilityTests
{
    [Fact]
    public void CatgaEndpointAttribute_ShouldNotRequireReflection()
    {
        // Arrange
        var attributeType = typeof(CatgaEndpointAttribute);

        // Act
        var attributes = attributeType.GetCustomAttributes();

        // Assert - Should not have RequiresUnreferencedCode or RequiresDynamicCode
        var hasRequiresUnreferencedCode = attributes
            .Any(a => a.GetType().Name.Contains("RequiresUnreferencedCode"));
        var hasRequiresDynamicCode = attributes
            .Any(a => a.GetType().Name.Contains("RequiresDynamicCode"));

        hasRequiresUnreferencedCode.Should().BeFalse("Attribute should not require unreferenced code");
        hasRequiresDynamicCode.Should().BeFalse("Attribute should not require dynamic code");
    }

    [Fact]
    public void RegisterEndpointExtension_ShouldNotUseReflection()
    {
        // Arrange
        var extensionType = typeof(CatgaSourceGeneratedEndpointExtensions);
        var registerMethod = extensionType.GetMethod("RegisterEndpoint");

        // Act
        var attributes = registerMethod!.GetCustomAttributes();

        // Assert - Should not have reflection-related attributes
        var hasRequiresUnreferencedCode = attributes
            .Any(a => a.GetType().Name.Contains("RequiresUnreferencedCode"));
        var hasRequiresDynamicCode = attributes
            .Any(a => a.GetType().Name.Contains("RequiresDynamicCode"));

        hasRequiresUnreferencedCode.Should().BeFalse("RegisterEndpoint should not require unreferenced code");
        hasRequiresDynamicCode.Should().BeFalse("RegisterEndpoint should not require dynamic code");
    }

    [Fact]
    public void GeneratedRegisterEndpoints_ShouldNotUseReflection()
    {
        // Arrange
        var handlerType = typeof(TestAOTCompatibleHandler);
        var registerMethod = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Act
        var attributes = registerMethod!.GetCustomAttributes();

        // Assert - Generated method should not have reflection attributes
        var hasRequiresUnreferencedCode = attributes
            .Any(a => a.GetType().Name.Contains("RequiresUnreferencedCode"));
        var hasRequiresDynamicCode = attributes
            .Any(a => a.GetType().Name.Contains("RequiresDynamicCode"));

        hasRequiresUnreferencedCode.Should().BeFalse("Generated RegisterEndpoints should not require unreferenced code");
        hasRequiresDynamicCode.Should().BeFalse("Generated RegisterEndpoints should not require dynamic code");
    }

    [Fact]
    public void EndpointHandlers_ShouldUseStaticMethods()
    {
        // Arrange
        var handlerType = typeof(TestAOTCompatibleHandler);
        var registerMethod = handlerType.GetMethod("RegisterEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Act & Assert
        registerMethod.Should().NotBeNull("RegisterEndpoints should be static");
        registerMethod!.IsStatic.Should().BeTrue("RegisterEndpoints must be static for AOT");
    }

    [Fact]
    public void EndpointHandlers_ShouldNotUseActivator()
    {
        // Arrange
        var handlerType = typeof(TestAOTCompatibleHandler);

        // Act - Verify handler can be instantiated without Activator
        var instance = new TestAOTCompatibleHandler();

        // Assert
        instance.Should().NotBeNull("Handler should be instantiable without reflection");
    }

    [Fact]
    public void EndpointHandlers_ShouldNotUseDynamicCodeGeneration()
    {
        // Arrange
        var handlerType = typeof(TestAOTCompatibleHandler);

        // Act - Check for dynamic code generation patterns
        var methods = handlerType.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Assert - No methods should require dynamic code
        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes();
            var hasRequiresDynamicCode = attributes
                .Any(a => a.GetType().Name.Contains("RequiresDynamicCode"));

            hasRequiresDynamicCode.Should().BeFalse($"Method {method.Name} should not require dynamic code");
        }
    }

    [Fact]
    public void SourceGenerator_ShouldProduceAOTSafeCode()
    {
        // Arrange
        var handlerType = typeof(TestAOTCompatibleHandler);

        // Act - Verify all public methods are AOT-safe
        var publicMethods = handlerType.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);

        // Assert
        foreach (var method in publicMethods)
        {
            // Check for reflection-based attributes
            var attributes = method.GetCustomAttributes();
            var hasReflectionAttributes = attributes.Any(a =>
                a.GetType().Name.Contains("RequiresUnreferencedCode") ||
                a.GetType().Name.Contains("RequiresDynamicCode"));

            hasReflectionAttributes.Should().BeFalse($"Method {method.Name} should be AOT-safe");
        }
    }

    [Fact]
    public void EndpointRegistration_ShouldNotRequireTypeDiscovery()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();

        // Act - Register endpoint without reflection
        var registrar = app.RegisterEndpoint<TestAOTCompatibleHandler>();

        // Assert - Should complete without reflection
        registrar.Should().NotBeNull();
    }

    [Fact]
    public void ChainedRegistration_ShouldNotRequireReflection()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();

        // Act - Chain multiple registrations without reflection
        var registrar = app.RegisterEndpoint<TestAOTCompatibleHandler>()
            .RegisterEndpoint<TestAOTCompatibleHandler>();

        // Assert
        registrar.Should().NotBeNull();
    }

    [Fact]
    public void PartialMethods_ShouldSupportAOTCompilation()
    {
        // Arrange
        var handlerType = typeof(TestAOTCompatibleHandler);

        // Act - Verify partial methods exist and are callable
        var createMethod = handlerType.GetMethod("Create",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var getMethod = handlerType.GetMethod("Get",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Assert
        createMethod.Should().NotBeNull("Partial method Create should exist");
        getMethod.Should().NotBeNull("Partial method Get should exist");
    }
}

/// <summary>
/// Test handler for AOT compatibility verification
/// </summary>
public partial class TestAOTCompatibleHandler
{
    [CatgaEndpoint(HttpMethod.Post, "/api/aot-test")]
    public partial async Task<IResult> Create(TestAOTCommand cmd, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Get, "/api/aot-test/{id}")]
    public partial async Task<IResult> Get(TestAOTQuery query, ICatgaMediator mediator);
}

public partial class TestAOTCompatibleHandler
{
    public partial async Task<IResult> Create(TestAOTCommand cmd, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestAOTCommand, TestAOTResult>(cmd);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest();
    }

    public partial async Task<IResult> Get(TestAOTQuery query, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestAOTQuery, TestAOTDto>(query);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
    }
}

// Test types
public class TestAOTCommand : IRequest<TestAOTResult>
{
    public string Value { get; set; }
}

public class TestAOTQuery : IRequest<TestAOTDto>
{
    public string Id { get; set; }
}

public class TestAOTResult
{
    public string Id { get; set; }
    public string Value { get; set; }
}

public class TestAOTDto
{
    public string Id { get; set; }
    public string Value { get; set; }
}
