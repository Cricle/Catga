using System.Runtime.CompilerServices;
using Catga.Exceptions;

namespace Catga.Core;

/// <summary>Result metadata (lightweight, pooled for performance)</summary>
public sealed class ResultMetadata
{
    private readonly Dictionary<string, string> _data;

    public ResultMetadata() => _data = new Dictionary<string, string>(4);

    [MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Add(string key, string value) => _data[key] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(string key, out string? value) => _data.TryGetValue(key, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(string key) => _data.ContainsKey(key);

    public IReadOnlyDictionary<string, string> GetAll() => _data;

    public int Count => _data.Count;
}

/// <summary>Catga operation result with value (zero-allocation struct for performance)</summary>
public readonly record struct CatgaResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }

    public static CatgaResult<T> Success(T value, ResultMetadata? metadata = null)
        => new() { IsSuccess = true, Value = value, Metadata = metadata };

    public static CatgaResult<T> Failure(string error, CatgaException? exception = null)
        => new() { IsSuccess = false, Error = error, Exception = exception };
}

/// <summary>Catga operation result without value (zero-allocation struct for performance)</summary>
public readonly record struct CatgaResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }

    public static CatgaResult Success(ResultMetadata? metadata = null)
        => new() { IsSuccess = true, Metadata = metadata };

    public static CatgaResult Failure(string error, CatgaException? exception = null)
        => new() { IsSuccess = false, Error = error, Exception = exception };
}
