using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Catga.Threading;

/// <summary>
/// Lightweight zero-allocation task alternative to Task
/// Inspired by UniTask - uses struct-based design for zero GC allocation
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CatgaTask
{
    private readonly ICatgaTaskSource? _source;
    private readonly short _token;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTask(ICatgaTaskSource source, short token)
    {
        _source = source;
        _token = token;
    }

    public CatgaTaskStatus Status
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _source?.GetStatus(_token) ?? CatgaTaskStatus.Succeeded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTaskAwaiter GetAwaiter() => new CatgaTaskAwaiter(this);

    public static CatgaTask CompletedTask => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetResult()
    {
        _source?.GetResult(_token);
    }
}

/// <summary>
/// Lightweight zero-allocation task with result
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CatgaTask<T>
{
    private readonly ICatgaTaskSource<T>? _source;
    private readonly short _token;
    private readonly T? _result;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTask(T result)
    {
        _source = null;
        _token = 0;
        _result = result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTask(ICatgaTaskSource<T> source, short token)
    {
        _source = source;
        _token = token;
        _result = default;
    }

    public CatgaTaskStatus Status
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _source?.GetStatus(_token) ?? CatgaTaskStatus.Succeeded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTaskAwaiter<T> GetAwaiter() => new CatgaTaskAwaiter<T>(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetResult()
    {
        return _source != null ? _source.GetResult(_token) : _result!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator CatgaTask<T>(T value) => new CatgaTask<T>(value);
}

/// <summary>
/// Task status enumeration
/// </summary>
public enum CatgaTaskStatus : byte
{
    Pending,
    Succeeded,
    Faulted,
    Canceled
}

/// <summary>
/// Awaiter for CatgaTask
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CatgaTaskAwaiter : ICriticalNotifyCompletion
{
    private readonly CatgaTask _task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTaskAwaiter(CatgaTask task)
    {
        _task = task;
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _task.Status != CatgaTaskStatus.Pending;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetResult() => _task.GetResult();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        UnsafeOnCompleted(continuation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation)
    {
        // Continuation is called when task completes
        if (IsCompleted)
        {
            continuation();
        }
        else
        {
            // Schedule continuation (simplified - in production would use source)
            Task.Run(continuation);
        }
    }
}

/// <summary>
/// Awaiter for CatgaTask{T}
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CatgaTaskAwaiter<T> : ICriticalNotifyCompletion
{
    private readonly CatgaTask<T> _task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CatgaTaskAwaiter(CatgaTask<T> task)
    {
        _task = task;
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _task.Status != CatgaTaskStatus.Pending;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetResult() => _task.GetResult();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        UnsafeOnCompleted(continuation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation)
    {
        if (IsCompleted)
        {
            continuation();
        }
        else
        {
            Task.Run(continuation);
        }
    }
}

/// <summary>
/// Task source interface (similar to IValueTaskSource)
/// </summary>
public interface ICatgaTaskSource
{
    CatgaTaskStatus GetStatus(short token);
    void GetResult(short token);
}

/// <summary>
/// Task source interface with result
/// </summary>
public interface ICatgaTaskSource<T>
{
    CatgaTaskStatus GetStatus(short token);
    T GetResult(short token);
}

