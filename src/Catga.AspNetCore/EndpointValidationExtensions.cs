using Catga.Abstractions;
using Catga.Core;
using Microsoft.AspNetCore.Http;

namespace Catga.AspNetCore;

/// <summary>
/// Validation extensions for Catga endpoint handlers.
/// Provides fluent validation and error handling patterns.
/// </summary>
public static class EndpointValidationExtensions
{
    /// <summary>
    /// Validate request and return error result if validation fails.
    /// </summary>
    public static async Task<IResult> ValidateAndExecuteAsync<TRequest, TResponse>(
        this TRequest request,
        Func<TRequest, Task<CatgaResult<TResponse>>> handler,
        Func<TRequest, (bool IsValid, string? Error)>? validator = null)
        where TRequest : class
    {
        // Validate request if validator provided
        if (validator != null)
        {
            var (isValid, error) = validator(request);
            if (!isValid)
                return Results.BadRequest(new { error = error ?? "Validation failed" });
        }

        // Execute handler
        var result = await handler(request);

        // Return appropriate result
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Validate request with multiple validators.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateMultiple<TRequest>(
        this TRequest request,
        params Func<TRequest, (bool IsValid, string? Error)>[] validators)
        where TRequest : class
    {
        foreach (var validator in validators)
        {
            var (isValid, error) = validator(request);
            if (!isValid)
                return (false, error);
        }

        return (true, null);
    }

    /// <summary>
    /// Validate that a string property is not null or empty.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateRequired(
        this string? value,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (false, $"{fieldName} is required");

        return (true, null);
    }

    /// <summary>
    /// Validate that a string property has minimum length.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateMinLength(
        this string? value,
        int minLength,
        string fieldName)
    {
        if (string.IsNullOrEmpty(value) || value.Length < minLength)
            return (false, $"{fieldName} must be at least {minLength} characters");

        return (true, null);
    }

    /// <summary>
    /// Validate that a string property has maximum length.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateMaxLength(
        this string? value,
        int maxLength,
        string fieldName)
    {
        if (value != null && value.Length > maxLength)
            return (false, $"{fieldName} must not exceed {maxLength} characters");

        return (true, null);
    }

    /// <summary>
    /// Validate that a numeric value is positive.
    /// </summary>
    public static (bool IsValid, string? Error) ValidatePositive(
        this decimal value,
        string fieldName)
    {
        if (value <= 0)
            return (false, $"{fieldName} must be positive");

        return (true, null);
    }

    /// <summary>
    /// Validate that a numeric value is within range.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateRange(
        this decimal value,
        decimal min,
        decimal max,
        string fieldName)
    {
        if (value < min || value > max)
            return (false, $"{fieldName} must be between {min} and {max}");

        return (true, null);
    }

    /// <summary>
    /// Validate that a collection is not empty.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateNotEmpty<T>(
        this IEnumerable<T>? collection,
        string fieldName)
    {
        if (collection == null || !collection.Any())
            return (false, $"{fieldName} must not be empty");

        return (true, null);
    }

    /// <summary>
    /// Validate that a collection has minimum count.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateMinCount<T>(
        this IEnumerable<T>? collection,
        int minCount,
        string fieldName)
    {
        if (collection == null || collection.Count() < minCount)
            return (false, $"{fieldName} must have at least {minCount} items");

        return (true, null);
    }
}

/// <summary>
/// Validation result builder for fluent validation.
/// </summary>
public class ValidationBuilder
{
    private readonly List<string> _errors = new();

    /// <summary>
    /// Add validation error.
    /// </summary>
    public ValidationBuilder AddError(string error)
    {
        if (!string.IsNullOrEmpty(error))
            _errors.Add(error);
        return this;
    }

    /// <summary>
    /// Add validation error if condition is true.
    /// </summary>
    public ValidationBuilder AddErrorIf(bool condition, string error)
    {
        if (condition)
            _errors.Add(error);
        return this;
    }

    /// <summary>
    /// Check if validation passed.
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Get all validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();

    /// <summary>
    /// Get first validation error.
    /// </summary>
    public string? FirstError => _errors.FirstOrDefault();

    /// <summary>
    /// Return IResult based on validation status.
    /// </summary>
    public IResult ToResult()
    {
        if (IsValid)
            return Results.Ok();

        return Results.BadRequest(new { errors = _errors });
    }

    /// <summary>
    /// Return IResult with custom success response.
    /// </summary>
    public IResult ToResult<T>(T successValue)
    {
        if (IsValid)
            return Results.Ok(successValue);

        return Results.BadRequest(new { errors = _errors });
    }
}
