using Catga.Exceptions;

namespace Catga.Results;

/// <summary>
/// Metadata for transit results (AOT-compatible, pooled dictionary)
/// </summary>
public sealed class ResultMetadata
{
    // 预分配容量，减少扩容
    private readonly Dictionary<string, string> _data = new(4);

    public void Add(string key, string value) => _data[key] = value;
    public bool TryGetValue(string key, out string? value) => _data.TryGetValue(key, out value);
    public bool ContainsKey(string key) => _data.ContainsKey(key);
    public IReadOnlyDictionary<string, string> GetAll() => _data;
    
    // 重用实例
    public void Clear() => _data.Clear();
}

/// <summary>
/// Result of a Catga operation with value (100% AOT-compatible)
/// </summary>
public class CatgaResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }

    public static CatgaResult<T> Success(T value, ResultMetadata? metadata = null) =>
        new() { IsSuccess = true, Value = value, Metadata = metadata };

    public static CatgaResult<T> Failure(string error, CatgaException? exception = null) =>
        new() { IsSuccess = false, Error = error, Exception = exception };
}

/// <summary>
/// Result of a Catga operation without value (100% AOT-compatible)
/// </summary>
public class CatgaResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }

    public static CatgaResult Success(ResultMetadata? metadata = null) =>
        new() { IsSuccess = true, Metadata = metadata };

    public static CatgaResult Failure(string error, CatgaException? exception = null) =>
        new() { IsSuccess = false, Error = error, Exception = exception };
}
