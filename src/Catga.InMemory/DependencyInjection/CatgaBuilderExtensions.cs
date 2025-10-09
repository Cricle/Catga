using Catga.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Fluent API extensions for CatgaBuilder
/// Provides a more intuitive and chainable configuration experience
/// </summary>
public static class CatgaBuilderExtensions
{
    /// <summary>
    /// Configure Catga options with fluent API
    /// </summary>
    public static CatgaBuilder Configure(this CatgaBuilder builder, Action<CatgaOptions> configure)
    {
        // Access options from builder's internal field
        var optionsField = builder.GetType().GetField("_options",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (optionsField != null)
        {
            var options = (CatgaOptions?)optionsField.GetValue(builder);
            if (options != null)
            {
                configure(options);
            }
        }

        return builder;
    }

    /// <summary>
    /// Enable logging with default configuration
    /// </summary>
    public static CatgaBuilder WithLogging(this CatgaBuilder builder, bool enabled = true)
    {
        return builder.Configure(options => options.EnableLogging = enabled);
    }

    /// <summary>
    /// Enable circuit breaker with smart defaults
    /// </summary>
    public static CatgaBuilder WithCircuitBreaker(
        this CatgaBuilder builder,
        int failureThreshold = 5,
        int resetTimeoutSeconds = 30)
    {
        return builder.Configure(options =>
        {
            options.EnableCircuitBreaker = true;
            options.CircuitBreakerFailureThreshold = failureThreshold;
            options.CircuitBreakerResetTimeoutSeconds = resetTimeoutSeconds;
        });
    }

    /// <summary>
    /// Enable rate limiting with smart defaults
    /// </summary>
    public static CatgaBuilder WithRateLimiting(
        this CatgaBuilder builder,
        int requestsPerSecond = 1000,
        int burstCapacity = 100)
    {
        return builder.Configure(options =>
        {
            options.EnableRateLimiting = true;
            options.RateLimitRequestsPerSecond = requestsPerSecond;
            options.RateLimitBurstCapacity = burstCapacity;
        });
    }

    /// <summary>
    /// Set concurrency limit
    /// </summary>
    public static CatgaBuilder WithConcurrencyLimit(this CatgaBuilder builder, int maxConcurrentRequests)
    {
        return builder.Configure(options => options.MaxConcurrentRequests = maxConcurrentRequests);
    }

    /// <summary>
    /// Add all recommended production settings
    /// </summary>
    public static CatgaBuilder UseProductionDefaults(this CatgaBuilder builder)
    {
        return builder
            .WithLogging(true)
            .WithCircuitBreaker(failureThreshold: 5, resetTimeoutSeconds: 30)
            .WithRateLimiting(requestsPerSecond: 1000, burstCapacity: 100)
            .WithConcurrencyLimit(100);
    }

    /// <summary>
    /// Add development-friendly settings (more permissive)
    /// </summary>
    public static CatgaBuilder UseDevelopmentDefaults(this CatgaBuilder builder)
    {
        return builder
            .WithLogging(true)
            .Configure(options =>
            {
                options.EnableCircuitBreaker = false;
                options.EnableRateLimiting = false;
                options.MaxConcurrentRequests = 0; // Unlimited
            });
    }

    /// <summary>
    /// Validate configuration and throw if invalid
    /// </summary>
    public static CatgaBuilder ValidateConfiguration(this CatgaBuilder builder)
    {
        // Access options from builder
        var optionsField = builder.GetType().GetField("_options",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var options = optionsField != null ? (CatgaOptions?)optionsField.GetValue(builder) : null;
        if (options == null)
            return builder;

        var errors = new List<string>();

        // Validate rate limiting
        if (options.EnableRateLimiting)
        {
            if (options.RateLimitRequestsPerSecond <= 0)
                errors.Add("RateLimitRequestsPerSecond must be > 0");
            if (options.RateLimitBurstCapacity <= 0)
                errors.Add("RateLimitBurstCapacity must be > 0");
        }

        // Validate circuit breaker
        if (options.EnableCircuitBreaker)
        {
            if (options.CircuitBreakerFailureThreshold <= 0)
                errors.Add("CircuitBreakerFailureThreshold must be > 0");
            if (options.CircuitBreakerResetTimeoutSeconds <= 0)
                errors.Add("CircuitBreakerResetTimeoutSeconds must be > 0");
        }

        // Validate concurrency
        if (options.MaxConcurrentRequests < 0)
            errors.Add("MaxConcurrentRequests must be >= 0");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Catga configuration validation failed:\n{string.Join("\n", errors)}");
        }

        return builder;
    }
}

