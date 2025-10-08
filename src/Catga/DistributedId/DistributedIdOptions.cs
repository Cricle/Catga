namespace Catga.DistributedId;

/// <summary>
/// Distributed ID generator options
/// </summary>
public sealed class DistributedIdOptions
{
    /// <summary>
    /// Worker ID
    /// Default: 0
    /// </summary>
    public int WorkerId { get; set; } = 0;

    /// <summary>
    /// Auto-detect worker ID from environment
    /// Checks: WORKER_ID, POD_INDEX, HOSTNAME
    /// </summary>
    public bool AutoDetectWorkerId { get; set; } = true;

    /// <summary>
    /// Bit layout configuration
    /// Default: 41-10-12 (~69 years)
    /// </summary>
    public SnowflakeBitLayout Layout { get; set; } = SnowflakeBitLayout.Default;

    /// <summary>
    /// Validate options
    /// </summary>
    public void Validate()
    {
        Layout.Validate();

        if (WorkerId < 0 || WorkerId > Layout.MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkerId),
                $"Worker ID must be between 0 and {Layout.MaxWorkerId} for layout {Layout}");
        }
    }

    /// <summary>
    /// Get worker ID with auto-detection
    /// </summary>
    public int GetWorkerId()
    {
        if (!AutoDetectWorkerId)
        {
            return WorkerId;
        }

        // Try environment variable
        var envWorkerId = Environment.GetEnvironmentVariable("WORKER_ID")
                          ?? Environment.GetEnvironmentVariable("POD_INDEX");

        if (!string.IsNullOrEmpty(envWorkerId) && int.TryParse(envWorkerId, out var parsedId))
        {
            if (parsedId >= 0 && parsedId <= 1023)
            {
                return parsedId;
            }
        }

        // Try hostname hash (for Kubernetes pods)
        var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrEmpty(hostname))
        {
            var maxWorkers = (int)(Layout.MaxWorkerId + 1);
            return Math.Abs(hostname.GetHashCode()) % maxWorkers;
        }

        // Fallback to configured value
        return WorkerId;
    }
}

