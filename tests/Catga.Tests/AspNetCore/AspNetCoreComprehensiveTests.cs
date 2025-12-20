using Catga.AspNetCore;
using Catga.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.AspNetCore;

/// <summary>
/// Comprehensive tests for Catga.AspNetCore components
/// </summary>
public class AspNetCoreComprehensiveTests
{
    #region CatgaAspNetCoreOptions Tests

    [Fact]
    public void CatgaAspNetCoreOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new CatgaAspNetCoreOptions();
        
        options.EnableDashboard.Should().BeTrue();
        options.DashboardPathPrefix.Should().Be("/catga");
        options.AutoMapHealthEndpoints.Should().BeTrue();
        options.ErrorFormat.Should().Be(ErrorResponseFormat.Simple);
    }

    [Fact]
    public void CatgaAspNetCoreOptions_CustomValues_ShouldBeSet()
    {
        var options = new CatgaAspNetCoreOptions
        {
            EnableDashboard = false,
            DashboardPathPrefix = "/custom",
            AutoMapHealthEndpoints = false,
            ErrorFormat = ErrorResponseFormat.ProblemDetails
        };
        
        options.EnableDashboard.Should().BeFalse();
        options.DashboardPathPrefix.Should().Be("/custom");
        options.AutoMapHealthEndpoints.Should().BeFalse();
        options.ErrorFormat.Should().Be(ErrorResponseFormat.ProblemDetails);
    }

    [Fact]
    public void CatgaAspNetCoreOptions_WithExpression_ShouldCreateNewInstance()
    {
        var original = new CatgaAspNetCoreOptions();
        var modified = original with { EnableDashboard = false };
        
        original.EnableDashboard.Should().BeTrue();
        modified.EnableDashboard.Should().BeFalse();
    }

    #endregion

    #region ErrorResponseFormat Tests

    [Theory]
    [InlineData(ErrorResponseFormat.Simple, 0)]
    [InlineData(ErrorResponseFormat.ProblemDetails, 1)]
    public void ErrorResponseFormat_AllValues_ShouldHaveCorrectValue(ErrorResponseFormat format, int expectedValue)
    {
        ((int)format).Should().Be(expectedValue);
    }

    #endregion

    #region CatgaEndpointAttribute Tests

    [Fact]
    public void CatgaEndpointAttribute_Constructor_ShouldSetProperties()
    {
        var attr = new CatgaEndpointAttribute("POST", "/api/orders");
        
        attr.HttpMethod.Should().Be("POST");
        attr.Route.Should().Be("/api/orders");
        attr.Name.Should().BeNull();
        attr.Description.Should().BeNull();
    }

    [Fact]
    public void CatgaEndpointAttribute_WithOptionalProperties_ShouldBeSet()
    {
        var attr = new CatgaEndpointAttribute("GET", "/api/orders/{id}")
        {
            Name = "GetOrder",
            Description = "Gets an order by ID"
        };
        
        attr.HttpMethod.Should().Be("GET");
        attr.Route.Should().Be("/api/orders/{id}");
        attr.Name.Should().Be("GetOrder");
        attr.Description.Should().Be("Gets an order by ID");
    }

    [Fact]
    public void CatgaEndpointAttribute_ShouldBeApplicableToMethod()
    {
        var attrUsage = typeof(CatgaEndpointAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void CatgaEndpointAttribute_AllHttpMethods_ShouldWork(string method)
    {
        var attr = new CatgaEndpointAttribute(method, "/api/test");
        
        attr.HttpMethod.Should().Be(method);
    }

    #endregion

    #region CatgaResultExtensions.ToHttpResult Tests

    [Fact]
    public void ToHttpResult_WithSuccessAndValue_ShouldReturnOk()
    {
        var result = CatgaResult<string>.Success("test value");
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<Ok<string>>();
        var okResult = (Ok<string>)httpResult;
        okResult.Value.Should().Be("test value");
    }

    [Fact]
    public void ToHttpResult_WithSuccessAndNullValue_ShouldReturnNoContent()
    {
        var result = CatgaResult<string?>.Success(null);
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToHttpResult_WithValidationFailed_ShouldReturnUnprocessableEntity()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.ValidationFailed,
            Message = "Validation error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.GetType().Name.Should().StartWith("UnprocessableEntity");
    }

    [Fact]
    public void ToHttpResult_WithTimeout_ShouldReturnProblem408()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.Timeout,
            Message = "Timeout error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public void ToHttpResult_WithCancelled_ShouldReturnProblem408()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.Cancelled,
            Message = "Cancelled"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public void ToHttpResult_WithHandlerFailed_ShouldReturnBadRequest()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.HandlerFailed,
            Message = "Handler error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.GetType().Name.Should().StartWith("BadRequest");
    }

    [Fact]
    public void ToHttpResult_WithPipelineFailed_ShouldReturnBadRequest()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.PipelineFailed,
            Message = "Pipeline error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.GetType().Name.Should().StartWith("BadRequest");
    }

    [Fact]
    public void ToHttpResult_WithPersistenceFailed_ShouldReturnProblem503()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.PersistenceFailed,
            Message = "Persistence error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public void ToHttpResult_WithLockFailed_ShouldReturnProblem503()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.LockFailed,
            Message = "Lock error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public void ToHttpResult_WithTransportFailed_ShouldReturnProblem503()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.TransportFailed,
            Message = "Transport error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public void ToHttpResult_WithSerializationFailed_ShouldReturnBadRequest()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.SerializationFailed,
            Message = "Serialization error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.GetType().Name.Should().StartWith("BadRequest");
    }

    [Fact]
    public void ToHttpResult_WithUnknownError_ShouldReturnBadRequest()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = "UNKNOWN_ERROR",
            Message = "Unknown error"
        });
        
        var httpResult = result.ToHttpResult();
        
        httpResult.GetType().Name.Should().StartWith("BadRequest");
    }

    #endregion

    #region CatgaResultExtensions.ToHttpResult with StatusCode Tests

    [Fact]
    public void ToHttpResult_WithSuccessStatusCode201_ShouldReturnCreated()
    {
        var result = CatgaResult<string>.Success("created");
        
        var httpResult = result.ToHttpResult(201);
        
        httpResult.Should().BeOfType<Created<string>>();
    }

    [Fact]
    public void ToHttpResult_WithSuccessStatusCode202_ShouldReturnAccepted()
    {
        var result = CatgaResult<string>.Success("accepted");
        
        var httpResult = result.ToHttpResult(202);
        
        httpResult.Should().BeOfType<Accepted<string>>();
    }

    [Fact]
    public void ToHttpResult_WithSuccessStatusCode204_ShouldReturnNoContent()
    {
        var result = CatgaResult<string>.Success("ignored");
        
        var httpResult = result.ToHttpResult(204);
        
        httpResult.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToHttpResult_WithSuccessStatusCodeOther_ShouldReturnStatusCode()
    {
        var result = CatgaResult<string>.Success("custom");
        
        var httpResult = result.ToHttpResult(299);
        
        httpResult.Should().BeOfType<StatusCodeHttpResult>();
    }

    [Fact]
    public void ToHttpResult_WithFailureAndStatusCode_ShouldReturnError()
    {
        var result = CatgaResult<string>.Failure(new ErrorInfo
        {
            Code = ErrorCodes.ValidationFailed,
            Message = "Error"
        });
        
        var httpResult = result.ToHttpResult(201);
        
        httpResult.GetType().Name.Should().StartWith("UnprocessableEntity");
    }

    #endregion

    #region CatgaResultHttpExtensions Tests

    [Fact]
    public void NotFound_ShouldCreateFailureWithNotFoundCode()
    {
        var result = CatgaResultHttpExtensions.NotFound<string>("Not found");
        
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Not found");
        result.ErrorCode.Should().Be(CatgaResultHttpExtensions.HttpNotFound);
    }

    [Fact]
    public void Conflict_ShouldCreateFailureWithConflictCode()
    {
        var result = CatgaResultHttpExtensions.Conflict<string>("Conflict");
        
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Conflict");
        result.ErrorCode.Should().Be(CatgaResultHttpExtensions.HttpConflict);
    }

    [Fact]
    public void ValidationError_ShouldCreateFailureWithValidationCode()
    {
        var result = CatgaResultHttpExtensions.ValidationError<string>("Validation error");
        
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Validation error");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Unauthorized_ShouldCreateFailureWithUnauthorizedCode()
    {
        var result = CatgaResultHttpExtensions.Unauthorized<string>("Unauthorized");
        
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Unauthorized");
        result.ErrorCode.Should().Be(CatgaResultHttpExtensions.HttpUnauthorized);
    }

    [Fact]
    public void Forbidden_ShouldCreateFailureWithForbiddenCode()
    {
        var result = CatgaResultHttpExtensions.Forbidden<string>("Forbidden");
        
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Forbidden");
        result.ErrorCode.Should().Be(CatgaResultHttpExtensions.HttpForbidden);
    }

    #endregion

    #region CatgaResultHttpConversionExtensions.ToHttpResultEx Tests

    [Fact]
    public void ToHttpResultEx_WithSuccess_ShouldReturnOk()
    {
        var result = CatgaResult<string>.Success("value");
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.Should().BeOfType<Ok<string>>();
    }

    [Fact]
    public void ToHttpResultEx_WithSuccessNull_ShouldReturnNoContent()
    {
        var result = CatgaResult<string?>.Success(null);
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToHttpResultEx_WithNotFound_ShouldReturnNotFound()
    {
        var result = CatgaResultHttpExtensions.NotFound<string>("Not found");
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.GetType().Name.Should().StartWith("NotFound");
    }

    [Fact]
    public void ToHttpResultEx_WithConflict_ShouldReturnConflict()
    {
        var result = CatgaResultHttpExtensions.Conflict<string>("Conflict");
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.GetType().Name.Should().StartWith("Conflict");
    }

    [Fact]
    public void ToHttpResultEx_WithUnauthorized_ShouldReturnUnauthorized()
    {
        var result = CatgaResultHttpExtensions.Unauthorized<string>("Unauthorized");
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public void ToHttpResultEx_WithForbidden_ShouldReturnForbid()
    {
        var result = CatgaResultHttpExtensions.Forbidden<string>("Forbidden");
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.Should().BeOfType<ForbidHttpResult>();
    }

    [Fact]
    public void ToHttpResultEx_WithValidationFailed_ShouldReturnUnprocessableEntity()
    {
        var result = CatgaResultHttpExtensions.ValidationError<string>("Validation error");
        
        var httpResult = result.ToHttpResultEx();
        
        httpResult.GetType().Name.Should().StartWith("UnprocessableEntity");
    }

    #endregion
}
