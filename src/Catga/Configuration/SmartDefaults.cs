using System.Runtime.InteropServices;

namespace Catga.Configuration;

/// <summary>
/// Smart defaults based on environment and system resources
/// Automatically configures Catga for optimal performance
/// </summary>
public static class SmartDefaults
{
    /// <summary>
    /// Get smart defaults based on current environment
    /// </summary>
    public static CatgaOptions GetEnvironmentDefaults()
    {
        var isDevelopment = IsDevelopmentEnvironment();
        var processorCount = Environment.ProcessorCount;
        var availableMemory = GetAvailableMemoryMB();

        var options = new CatgaOptions
        {
            EnableLogging = true
        };

        if (isDevelopment)
        {
            // Development: More permissive, easier debugging
            options.EnableCircuitBreaker = false;
            options.EnableRateLimiting = false;
            options.MaxConcurrentRequests = 0; // Unlimited
        }
        else
        {
            // Production: Conservative, stable defaults
            options.EnableCircuitBreaker = true;
            options.CircuitBreakerFailureThreshold = 5;
            options.CircuitBreakerResetTimeoutSeconds = 30;

            options.EnableRateLimiting = true;
            options.RateLimitRequestsPerSecond = 1000;
            options.RateLimitBurstCapacity = 100;

            // Scale concurrency with CPU cores
            options.MaxConcurrentRequests = processorCount * 25; // ~100 for 4-core
        }

        return options;
    }

    /// <summary>
    /// Get high-performance defaults for production
    /// </summary>
    public static CatgaOptions GetHighPerformanceDefaults()
    {
        var processorCount = Environment.ProcessorCount;

        return new CatgaOptions
        {
            EnableLogging = false, // Minimal overhead
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 10, // Higher threshold
            CircuitBreakerResetTimeoutSeconds = 15, // Faster recovery

            EnableRateLimiting = true,
            RateLimitRequestsPerSecond = 5000, // High throughput
            RateLimitBurstCapacity = 500,

            MaxConcurrentRequests = processorCount * 50 // Aggressive
        };
    }

    /// <summary>
    /// Get conservative defaults for stability
    /// </summary>
    public static CatgaOptions GetConservativeDefaults()
    {
        var processorCount = Environment.ProcessorCount;

        return new CatgaOptions
        {
            EnableLogging = true,
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 3, // Lower threshold
            CircuitBreakerResetTimeoutSeconds = 60, // Slower recovery

            EnableRateLimiting = true,
            RateLimitRequestsPerSecond = 500, // Conservative
            RateLimitBurstCapacity = 50,

            MaxConcurrentRequests = processorCount * 10 // Conservative
        };
    }

    /// <summary>
    /// Get defaults for microservices
    /// </summary>
    public static CatgaOptions GetMicroserviceDefaults()
    {
        var processorCount = Environment.ProcessorCount;

        return new CatgaOptions
        {
            EnableLogging = true,
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerResetTimeoutSeconds = 20, // Fast recovery

            EnableRateLimiting = true,
            RateLimitRequestsPerSecond = 2000,
            RateLimitBurstCapacity = 200,

            MaxConcurrentRequests = processorCount * 30
        };
    }

    /// <summary>
    /// Check if running in development environment
    /// </summary>
    private static bool IsDevelopmentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get available memory in MB (approximation)
    /// </summary>
    private static long GetAvailableMemoryMB()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Could use GlobalMemoryStatusEx, simplified here
                return 4096; // Assume 4GB default
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: Could read /proc/meminfo, simplified here
                return 4096;
            }
            else
            {
                return 4096; // Default
            }
        }
        catch
        {
            return 4096; // Fallback
        }
    }

    /// <summary>
    /// Auto-tune based on system resources
    /// </summary>
    public static CatgaOptions AutoTune()
    {
        var processorCount = Environment.ProcessorCount;
        var memoryMB = GetAvailableMemoryMB();
        var isDevelopment = IsDevelopmentEnvironment();

        var options = new CatgaOptions
        {
            EnableLogging = true
        };

        if (isDevelopment)
        {
            return GetEnvironmentDefaults();
        }

        // Auto-tune based on resources
        if (processorCount >= 8 && memoryMB >= 8192)
        {
            // High-end server
            return GetHighPerformanceDefaults();
        }
        else if (processorCount >= 4 && memoryMB >= 4096)
        {
            // Medium server
            return GetMicroserviceDefaults();
        }
        else
        {
            // Low-end or container
            return GetConservativeDefaults();
        }
    }
}

