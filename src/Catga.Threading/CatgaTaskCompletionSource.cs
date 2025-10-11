using System.Runtime.CompilerServices;

namespace Catga.Threading;

/// <summary>
/// Zero-allocation task completion source
/// Pooled and reused to avoid GC allocation
/// </summary>
public sealed class CatgaTaskCompletionSource : ICatgaTaskSource
{
    private static readonly TaskPool<CatgaTaskCompletionSource> Pool = new();

    private Action? _continuation;
    private CatgaTaskStatus _status;
    private Exception? _exception;
    private short _version;

    private CatgaTaskCompletionSource() { }

    public static CatgaTaskCompletionSource Create()
    {
        if (!Pool.TryPop(out var source))
        {
            source = new CatgaTaskCompletionSource();
        }
        source._status = CatgaTaskStatus.Pending;
        source._exception = null;
        source._continuation = null;
        return source;
    }

    public CatgaTask Task => new CatgaTask(this, _version);

    public void SetResult()
    {
        _status = CatgaTaskStatus.Succeeded;
        TryInvokeContinuation();
    }

    public void SetException(Exception exception)
    {
        _exception = exception;
        _status = CatgaTaskStatus.Faulted;
        TryInvokeContinuation();
    }

    public void SetCanceled()
    {
        _status = CatgaTaskStatus.Canceled;
        TryInvokeContinuation();
    }

    public CatgaTaskStatus GetStatus(short token)
    {
        ValidateToken(token);
        return _status;
    }

    public void GetResult(short token)
    {
        ValidateToken(token);

        if (_status == CatgaTaskStatus.Faulted && _exception != null)
        {
            throw _exception;
        }

        if (_status == CatgaTaskStatus.Canceled)
        {
            throw new OperationCanceledException();
        }

        // Return to pool
        TryReturn();
    }

    public void OnCompleted(Action continuation, short token)
    {
        ValidateToken(token);

        if (_status != CatgaTaskStatus.Pending)
        {
            continuation();
            return;
        }

        _continuation = continuation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateToken(short token)
    {
        if (token != _version)
        {
            throw new InvalidOperationException("Token version mismatch");
        }
    }

    private void TryInvokeContinuation()
    {
        var continuation = _continuation;
        _continuation = null;
        continuation?.Invoke();
    }

    private void TryReturn()
    {
        unchecked
        {
            _version++;
        }
        Pool.TryPush(this);
    }
}

/// <summary>
/// Zero-allocation task completion source with result
/// </summary>
public sealed class CatgaTaskCompletionSource<T> : ICatgaTaskSource<T>
{
    private static readonly TaskPool<CatgaTaskCompletionSource<T>> Pool = new();

    private Action? _continuation;
    private CatgaTaskStatus _status;
    private T? _result;
    private Exception? _exception;
    private short _version;

    private CatgaTaskCompletionSource() { }

    public static CatgaTaskCompletionSource<T> Create()
    {
        if (!Pool.TryPop(out var source))
        {
            source = new CatgaTaskCompletionSource<T>();
        }
        source._status = CatgaTaskStatus.Pending;
        source._result = default;
        source._exception = null;
        source._continuation = null;
        return source;
    }

    public CatgaTask<T> Task => new CatgaTask<T>(this, _version);

    public void SetResult(T result)
    {
        _result = result;
        _status = CatgaTaskStatus.Succeeded;
        TryInvokeContinuation();
    }

    public void SetException(Exception exception)
    {
        _exception = exception;
        _status = CatgaTaskStatus.Faulted;
        TryInvokeContinuation();
    }

    public void SetCanceled()
    {
        _status = CatgaTaskStatus.Canceled;
        TryInvokeContinuation();
    }

    public CatgaTaskStatus GetStatus(short token)
    {
        ValidateToken(token);
        return _status;
    }

    public T GetResult(short token)
    {
        ValidateToken(token);

        if (_status == CatgaTaskStatus.Faulted && _exception != null)
        {
            throw _exception;
        }

        if (_status == CatgaTaskStatus.Canceled)
        {
            throw new OperationCanceledException();
        }

        var result = _result!;

        // Return to pool
        TryReturn();

        return result;
    }

    public void OnCompleted(Action continuation, short token)
    {
        ValidateToken(token);

        if (_status != CatgaTaskStatus.Pending)
        {
            continuation();
            return;
        }

        _continuation = continuation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateToken(short token)
    {
        if (token != _version)
        {
            throw new InvalidOperationException("Token version mismatch");
        }
    }

    private void TryInvokeContinuation()
    {
        var continuation = _continuation;
        _continuation = null;
        continuation?.Invoke();
    }

    private void TryReturn()
    {
        unchecked
        {
            _version++;
        }
        _result = default;
        Pool.TryPush(this);
    }
}

/// <summary>
/// Simple object pool for task completion sources
/// </summary>
internal class TaskPool<T> where T : class
{
    private readonly System.Collections.Concurrent.ConcurrentBag<T> _items = new();
    private const int MaxSize = 1000;

    public bool TryPop(out T? item)
    {
        return _items.TryTake(out item);
    }

    public bool TryPush(T item)
    {
        if (_items.Count < MaxSize)
        {
            _items.Add(item);
            return true;
        }
        return false;
    }
}

