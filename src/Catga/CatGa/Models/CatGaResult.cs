namespace Catga.CatGa.Models;

/// <summary>
/// CatGa 执行结果
/// </summary>
/// <typeparam name="T">结果类型</typeparam>
public readonly struct CatGaResult<T>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 结果值
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 是否已补偿
    /// </summary>
    public bool IsCompensated { get; init; }

    /// <summary>
    /// 事务上下文
    /// </summary>
    public CatGaContext? Context { get; init; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static CatGaResult<T> Success(T value, CatGaContext? context = null) => new()
    {
        IsSuccess = true,
        Value = value,
        Context = context
    };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static CatGaResult<T> Failure(string error, CatGaContext? context = null) => new()
    {
        IsSuccess = false,
        Error = error,
        Context = context
    };

    /// <summary>
    /// 创建已补偿结果
    /// </summary>
    public static CatGaResult<T> Compensated(string error, CatGaContext? context = null) => new()
    {
        IsSuccess = false,
        Error = error,
        IsCompensated = true,
        Context = context
    };
}

/// <summary>
/// CatGa 无返回值结果
/// </summary>
public readonly struct CatGaResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public bool IsCompensated { get; init; }
    public CatGaContext? Context { get; init; }

    public static CatGaResult Success(CatGaContext? context = null) => new()
    {
        IsSuccess = true,
        Context = context
    };

    public static CatGaResult Failure(string error, CatGaContext? context = null) => new()
    {
        IsSuccess = false,
        Error = error,
        Context = context
    };

    public static CatGaResult Compensated(string error, CatGaContext? context = null) => new()
    {
        IsSuccess = false,
        Error = error,
        IsCompensated = true,
        Context = context
    };
}

