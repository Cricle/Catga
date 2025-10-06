
using System.Buffers;

namespace Catga.ObjectPool;

/// <summary>
/// 🔥 批量缓冲区对象池 - 用于批量操作的高性能缓冲
/// </summary>
public static class BatchBufferPool
{
    /// <summary>
    /// 租用一个用于批量结果的数组
    /// </summary>
    public static T[] Rent<T>(int minimumLength)
    {
        return ArrayPool<T>.Shared.Rent(minimumLength);
    }

    /// <summary>
    /// 归还批量结果数组
    /// </summary>
    public static void Return<T>(T[] array, bool clearArray = false)
    {
        ArrayPool<T>.Shared.Return(array, clearArray);
    }

    /// <summary>
    /// 租用 ValueTask 数组用于并行执行
    /// </summary>
    public static ValueTask<T>[] RentValueTaskArray<T>(int minimumLength)
    {
        return ArrayPool<ValueTask<T>>.Shared.Rent(minimumLength);
    }

    /// <summary>
    /// 归还 ValueTask 数组
    /// </summary>
    public static void ReturnValueTaskArray<T>(ValueTask<T>[] array)
    {
        ArrayPool<ValueTask<T>>.Shared.Return(array, clearArray: true);
    }

    /// <summary>
    /// 租用 Task 数组用于并行执行
    /// </summary>
    public static Task<T>[] RentTaskArray<T>(int minimumLength)
    {
        return ArrayPool<Task<T>>.Shared.Rent(minimumLength);
    }

    /// <summary>
    /// 归还 Task 数组
    /// </summary>
    public static void ReturnTaskArray<T>(Task<T>[] array)
    {
        ArrayPool<Task<T>>.Shared.Return(array, clearArray: true);
    }
}

