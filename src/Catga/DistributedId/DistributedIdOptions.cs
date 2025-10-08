namespace Catga.DistributedId;

/// <summary>
/// Distributed ID generator options
/// </summary>
public sealed class DistributedIdOptions
{
    /// <summary>
    /// Worker ID (0-1023)
    /// Default: 0
    /// </summary>
    public int WorkerId { get; set; } = 0;

    /// <summary>
    /// Auto-detect worker ID from environment
    /// Checks: WORKER_ID, POD_INDEX, HOSTNAME
    /// </summary>
    public bool AutoDetectWorkerId { get; set; } = true;

    /// <summary>
    /// Validate options
    /// </summary>
    public void Validate()
    {
        if (WorkerId < 0 || WorkerId > 1023)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkerId),
                "Worker ID must be between 0 and 1023");
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
            return Math.Abs(hostname.GetHashCode()) % 1024;
        }

        // Fallback to configured value
        return WorkerId;
    }
}

