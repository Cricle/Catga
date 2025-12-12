namespace Catga.Flow.Dsl;

/// <summary>
/// Result of a flow step execution.
/// </summary>
public readonly struct StepResult
{
    public bool Success { get; }
    public bool Skipped { get; }
    public string? Error { get; }
    public object? Result { get; }
    public bool IsSuspended { get; }

    private StepResult(bool success, bool skipped, bool suspended, string? error, object? result)
    {
        Success = success;
        Skipped = skipped;
        IsSuspended = suspended;
        Error = error;
        Result = result;
    }

    public static StepResult Succeeded(object? result = null) => new(true, false, false, null, result);
    public static StepResult Failed(string error) => new(false, false, false, error, null);
    public static StepResult Skip() => new(true, true, false, null, null);
    public static StepResult Suspended() => new(true, false, true, null, null);
}
