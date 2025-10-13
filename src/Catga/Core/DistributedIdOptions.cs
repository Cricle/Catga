namespace Catga.DistributedId;

/// <summary>Distributed ID generator options</summary>
public class DistributedIdOptions
{
    public int WorkerId { get; set; } = 0;
    public bool AutoDetectWorkerId { get; set; } = true;
    public SnowflakeBitLayout Layout { get; set; } = SnowflakeBitLayout.Default;
    public DateTime? CustomEpoch { get; set; }

    public SnowflakeBitLayout GetEffectiveLayout()
    {
        return CustomEpoch.HasValue
            ? SnowflakeBitLayout.Create(CustomEpoch.Value, Layout.TimestampBits, Layout.WorkerIdBits, Layout.SequenceBits)
            : Layout;
    }

    public void Validate()
    {
        Layout.Validate();
        if (WorkerId < 0 || WorkerId > Layout.MaxWorkerId)
            throw new ArgumentOutOfRangeException(nameof(WorkerId), $"Worker ID must be between 0 and {Layout.MaxWorkerId} for layout {Layout}");
    }

    public int GetWorkerId()
    {
        if (!AutoDetectWorkerId) return WorkerId;

        var envWorkerId = Environment.GetEnvironmentVariable("WORKER_ID") ?? Environment.GetEnvironmentVariable("POD_INDEX");
        if (!string.IsNullOrEmpty(envWorkerId) && int.TryParse(envWorkerId, out var parsedId))
        {
            if (parsedId >= 0 && parsedId <= 1023) return parsedId;
        }

        var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrEmpty(hostname))
        {
            var maxWorkers = (int)(Layout.MaxWorkerId + 1);
            return Math.Abs(hostname.GetHashCode()) % maxWorkers;
        }

        return WorkerId;
    }
}

