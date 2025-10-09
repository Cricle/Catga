namespace Catga.Configuration;

/// <summary>
/// Validates CatgaOptions configuration
/// Provides detailed error messages for misconfiguration
/// </summary>
public static class CatgaOptionsValidator
{
    /// <summary>
    /// Validate options and return validation result
    /// </summary>
    public static ValidationResult Validate(CatgaOptions options)
    {
        var errors = new List<ValidationError>();

        // Validate rate limiting
        if (options.EnableRateLimiting)
        {
            if (options.RateLimitRequestsPerSecond <= 0)
            {
                errors.Add(new ValidationError(
                    "RateLimitRequestsPerSecond",
                    options.RateLimitRequestsPerSecond.ToString(),
                    "Must be greater than 0",
                    "Set to a positive value, e.g., 1000"));
            }

            if (options.RateLimitBurstCapacity <= 0)
            {
                errors.Add(new ValidationError(
                    "RateLimitBurstCapacity",
                    options.RateLimitBurstCapacity.ToString(),
                    "Must be greater than 0",
                    "Set to a positive value, e.g., 100"));
            }

            if (options.RateLimitBurstCapacity > options.RateLimitRequestsPerSecond)
            {
                errors.Add(new ValidationError(
                    "RateLimitBurstCapacity",
                    options.RateLimitBurstCapacity.ToString(),
                    $"Should not exceed RateLimitRequestsPerSecond ({options.RateLimitRequestsPerSecond})",
                    "Reduce burst capacity or increase requests per second"));
            }
        }

        // Validate circuit breaker
        if (options.EnableCircuitBreaker)
        {
            if (options.CircuitBreakerFailureThreshold <= 0)
            {
                errors.Add(new ValidationError(
                    "CircuitBreakerFailureThreshold",
                    options.CircuitBreakerFailureThreshold.ToString(),
                    "Must be greater than 0",
                    "Set to a positive value, e.g., 5"));
            }

            if (options.CircuitBreakerResetTimeoutSeconds <= 0)
            {
                errors.Add(new ValidationError(
                    "CircuitBreakerResetTimeoutSeconds",
                    options.CircuitBreakerResetTimeoutSeconds.ToString(),
                    "Must be greater than 0",
                    "Set to a positive value, e.g., 30"));
            }

            if (options.CircuitBreakerResetTimeoutSeconds < 5)
            {
                errors.Add(new ValidationError(
                    "CircuitBreakerResetTimeoutSeconds",
                    options.CircuitBreakerResetTimeoutSeconds.ToString(),
                    "Should be at least 5 seconds for stability",
                    "Increase to at least 5 seconds",
                    ValidationErrorLevel.Warning));
            }
        }

        // Validate concurrency
        if (options.MaxConcurrentRequests < 0)
        {
            errors.Add(new ValidationError(
                "MaxConcurrentRequests",
                options.MaxConcurrentRequests.ToString(),
                "Must be >= 0 (0 = unlimited)",
                "Set to 0 for unlimited or a positive value"));
        }

        if (options.MaxConcurrentRequests > 10000)
        {
            errors.Add(new ValidationError(
                "MaxConcurrentRequests",
                options.MaxConcurrentRequests.ToString(),
                "Very high concurrency may cause resource exhaustion",
                "Consider a lower value or ensure sufficient resources",
                ValidationErrorLevel.Warning));
        }

        return new ValidationResult(errors);
    }

    /// <summary>
    /// Validate and throw if invalid
    /// </summary>
    public static void ValidateAndThrow(CatgaOptions options)
    {
        var result = Validate(options);
        if (!result.IsValid)
        {
            throw new CatgaConfigurationException(result);
        }
    }
}

/// <summary>
/// Validation result
/// </summary>
public class ValidationResult
{
    public IReadOnlyList<ValidationError> Errors { get; }
    public bool IsValid => !Errors.Any(e => e.Level == ValidationErrorLevel.Error);
    public bool HasWarnings => Errors.Any(e => e.Level == ValidationErrorLevel.Warning);

    public ValidationResult(IEnumerable<ValidationError> errors)
    {
        Errors = errors.ToList();
    }

    public override string ToString()
    {
        if (IsValid && !HasWarnings)
            return "Configuration is valid";

        var errorMessages = Errors
            .Select(e => $"[{e.Level}] {e.PropertyName}: {e.Message}\n  Current: {e.CurrentValue}\n  Suggestion: {e.Suggestion}");

        return string.Join("\n\n", errorMessages);
    }
}

/// <summary>
/// Validation error
/// </summary>
public class ValidationError
{
    public string PropertyName { get; }
    public string CurrentValue { get; }
    public string Message { get; }
    public string Suggestion { get; }
    public ValidationErrorLevel Level { get; }

    public ValidationError(
        string propertyName,
        string currentValue,
        string message,
        string suggestion,
        ValidationErrorLevel level = ValidationErrorLevel.Error)
    {
        PropertyName = propertyName;
        CurrentValue = currentValue;
        Message = message;
        Suggestion = suggestion;
        Level = level;
    }
}

/// <summary>
/// Validation error level
/// </summary>
public enum ValidationErrorLevel
{
    Warning,
    Error
}

/// <summary>
/// Configuration validation exception
/// </summary>
public class CatgaConfigurationException : Exception
{
    public ValidationResult ValidationResult { get; }

    public CatgaConfigurationException(ValidationResult result)
        : base($"Catga configuration validation failed:\n{result}")
    {
        ValidationResult = result;
    }
}

