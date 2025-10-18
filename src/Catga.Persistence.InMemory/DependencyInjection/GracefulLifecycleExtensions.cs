using Catga.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catga.DependencyInjection;

/// <summary>
/// Graceful lifecycle extensions - makes distributed apps as simple as monoliths
/// </summary>
public static class GracefulLifecycleExtensions
{
    /// <summary>
    /// Enable graceful shutdown and recovery - one line, fully automated
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCatga()
    ///     .UseGracefulLifecycle();
    /// </code>
    /// </example>
    public static CatgaBuilder UseGracefulLifecycle(this CatgaBuilder builder)
    {
        // Register shutdown manager
        builder.Services.AddSingleton<GracefulShutdownManager>();

        // Register recovery manager
        builder.Services.AddSingleton<GracefulRecoveryManager>();

        // Register lifecycle hosted service
        builder.Services.AddHostedService<GracefulLifecycleHostedService>();

        return builder;
    }

    /// <summary>
    /// Enable auto-recovery - retries on failure detection
    /// </summary>
    public static CatgaBuilder UseAutoRecovery(
        this CatgaBuilder builder,
        TimeSpan? checkInterval = null,
        int maxRetries = 3)
    {
        builder.UseGracefulLifecycle();

        builder.Services.Configure<AutoRecoveryOptions>(options =>
        {
            options.CheckInterval = checkInterval ?? TimeSpan.FromSeconds(30);
            options.MaxRetries = maxRetries;
        });

        return builder;
    }
}

/// <summary>
/// Lifecycle hosted service - auto-handles startup and shutdown
/// </summary>
internal sealed class GracefulLifecycleHostedService : IHostedService
{
    private readonly GracefulShutdownManager _shutdownManager;
    private readonly GracefulRecoveryManager _recoveryManager;
    private readonly ILogger<GracefulLifecycleHostedService> _logger;
    private readonly AutoRecoveryOptions _options;
    private Task? _autoRecoveryTask;

    public GracefulLifecycleHostedService(
        GracefulShutdownManager shutdownManager,
        GracefulRecoveryManager recoveryManager,
        ILogger<GracefulLifecycleHostedService> logger,
        IOptions<AutoRecoveryOptions> options)
    {
        _shutdownManager = shutdownManager;
        _recoveryManager = recoveryManager;
        _logger = logger;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Catga graceful lifecycle enabled");

        // Start auto-recovery loop if configured
        if (_options.CheckInterval > TimeSpan.Zero)
        {
            _autoRecoveryTask = _recoveryManager.StartAutoRecoveryAsync(
                _options.CheckInterval,
                _options.MaxRetries,
                _shutdownManager.ShutdownToken);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Catga graceful shutdown triggered");

        // Start graceful shutdown
        await _shutdownManager.ShutdownAsync(TimeSpan.FromSeconds(30), cancellationToken);

        // Wait for auto-recovery task to end
        if (_autoRecoveryTask != null)
        {
            try
            {
                await _autoRecoveryTask;
            }
            catch (OperationCanceledException)
            {
                // Expected cancellation
            }
        }

        _logger.LogInformation("Catga graceful shutdown complete");
    }
}

/// <summary>
/// Auto-recovery options
/// </summary>
internal sealed class AutoRecoveryOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.Zero;
    public int MaxRetries { get; set; } = 3;
}

