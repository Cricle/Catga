using Catga.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace Catga.AspNetCore.Validation;

/// <summary>
/// Validation extensions for Catga requests.
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// Validates a request using DataAnnotations attributes.
    /// </summary>
    public static ValidationResult ValidateRequest<T>(this T request) where T : class
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            return ValidationResult.Success;
        }

        var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
        return new ValidationResult(errors);
    }

    /// <summary>
    /// Validates a request asynchronously with custom validators.
    /// </summary>
    public static async Task<ValidationResult> ValidateRequestAsync<T>(
        this T request,
        IEnumerable<IRequestValidator<T>> validators) where T : class
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // First, run DataAnnotations validation
        if (!Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            return new ValidationResult(errors);
        }

        // Then, run custom validators
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request);
            if (!result.IsValid)
            {
                return new ValidationResult(result.ErrorMessage);
            }
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Interface for custom request validators.
/// </summary>
public interface IRequestValidator<T> where T : class
{
    Task<ValidationResultEx> ValidateAsync(T request);
}

/// <summary>
/// Extended validation result with additional metadata.
/// </summary>
public class ValidationResultEx
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string>? FieldErrors { get; set; }

    public static ValidationResultEx Success() => new() { IsValid = true };
    public static ValidationResultEx Failure(string message) => new() { IsValid = false, ErrorMessage = message };
    public static ValidationResultEx FieldFailure(Dictionary<string, string> fieldErrors) =>
        new() { IsValid = false, FieldErrors = fieldErrors };
}

/// <summary>
/// Validation attributes for common scenarios.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotEmptyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string str && string.IsNullOrWhiteSpace(str))
        {
            return new ValidationResult($"{validationContext.DisplayName} cannot be empty");
        }

        if (value is System.Collections.ICollection collection && collection.Count == 0)
        {
            return new ValidationResult($"{validationContext.DisplayName} cannot be empty");
        }

        return ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class PositiveAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is int intValue && intValue <= 0)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be positive");
        }

        if (value is decimal decimalValue && decimalValue <= 0)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be positive");
        }

        if (value is double doubleValue && doubleValue <= 0)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be positive");
        }

        return ValidationResult.Success;
    }
}
