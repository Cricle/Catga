using System.Text.Json.Serialization;
using MemoryPack;

namespace Catga.Flow.Dsl;

/// <summary>
/// Flow execution position supporting nested branches.
/// </summary>
[MemoryPackable]
public partial record FlowPosition
{
    /// <summary>Path through the flow. Each element is a step index within current scope.</summary>
    public int[] Path { get; init; }

    [MemoryPackConstructor]
    public FlowPosition(int[] path) => Path = path ?? [0];

    /// <summary>Create initial position at step 0.</summary>
    [MemoryPackIgnore]
    public static FlowPosition Initial => new([0]);

    /// <summary>Current step index (last element of path).</summary>
    [MemoryPackIgnore]
    public int CurrentIndex => Path.Length > 0 ? Path[^1] : 0;

    /// <summary>Depth in branch hierarchy (0 = top level).</summary>
    [MemoryPackIgnore]
    public int Depth => Path.Length - 1;

    /// <summary>Whether currently inside a branch.</summary>
    [MemoryPackIgnore]
    public bool IsInBranch => Path.Length > 1;

    /// <summary>Advance to next step in current scope.</summary>
    public FlowPosition Advance() => Path.Length == 0
        ? new([1])
        : new([.. Path[..^1], Path[^1] + 1]);

    /// <summary>Enter a branch at given step index.</summary>
    public FlowPosition EnterBranch(int stepIndex) => new([.. Path, stepIndex]);

    /// <summary>Exit current branch scope.</summary>
    public FlowPosition ExitBranch() => Path.Length <= 1
        ? this
        : new(Path[..^1]);

    /// <summary>Get parent position (one level up).</summary>
    [JsonIgnore]
    [MemoryPackIgnore]
    public FlowPosition Parent => ExitBranch();
}

/// <summary>Branch type for If/Switch.</summary>
public enum BranchType { Then, Else, Case, Default }

/// <summary>ForEach failure handling strategy.</summary>
public enum ForEachFailureHandling
{
    /// <summary>Stop on first failure.</summary>
    StopOnFirstFailure,
    /// <summary>Continue processing remaining items on failure.</summary>
    ContinueOnFailure
}

/// <summary>ForEach processing progress for recovery.</summary>
[MemoryPackable]
public partial record ForEachProgress
{
    /// <summary>Current item index being processed.</summary>
    public int CurrentIndex { get; init; }
    /// <summary>Total number of items to process.</summary>
    public int TotalCount { get; init; }
    /// <summary>Indices of successfully completed items.</summary>
    public List<int> CompletedIndices { get; init; } = [];
    /// <summary>Indices of failed items.</summary>
    public List<int> FailedIndices { get; init; } = [];
}
