using Catga.Exceptions;

namespace Catga.Results;

/// <summary>
/// Metadata for transit results (AOT-compatible)
/// </summary>
public sealed class ResultMetadata
{
    private readonly Dictionary<string, string> _data = new();

    public void Add(string key, string value) => _data[key] = value;
    public bool TryGetValue(string key, out string? value) => _data.TryGetValue(key, out value);
    public bool ContainsKey(string key) => _data.ContainsKey(key);
    public IReadOnlyDictionary<string, string> GetAll() => _data;
}

/// <summary>
/// Result of a transit operation with value (100% AOT-compatible)
/// </summary>
public class TransitResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public TransitException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }

    public static TransitResult<T> Success(T value, ResultMetadata? metadata = null) =>
        new() { IsSuccess = true, Value = value, Metadata = metadata };

    public static TransitResult<T> Failure(string error, TransitException? exception = null) =>
        new() { IsSuccess = false, Error = error, Exception = exception };
}

/// <summary>
/// Result of a transit operation without value (100% AOT-compatible)
/// </summary>
public class TransitResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public TransitException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }

    public static TransitResult Success(ResultMetadata? metadata = null) =>
        new() { IsSuccess = true, Metadata = metadata };

    public static TransitResult Failure(string error, TransitException? exception = null) =>
        new() { IsSuccess = false, Error = error, Exception = exception };
}
