using System;
using System.Text;
using Catga.Core;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for ArrayPoolHelper pooled encoding methods (AOT-safe, memory-safe)
/// </summary>
public class ArrayPoolHelperPooledTests
{
    #region GetBytesPooled Tests

    [Fact]
    public void GetBytesPooled_EmptyString_ReturnsEmptyArray()
    {
        using var result = ArrayPoolHelper.GetBytesPooled("");
        
        Assert.Equal(0, result.Count);
        Assert.NotNull(result.Array);
    }

    [Fact]
    public void GetBytesPooled_NullString_ReturnsEmptyArray()
    {
        using var result = ArrayPoolHelper.GetBytesPooled(null!);
        
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void GetBytesPooled_AsciiString_ReturnsCorrectBytes()
    {
        const string input = "Hello World";
        using var result = ArrayPoolHelper.GetBytesPooled(input);
        
        var expected = Encoding.UTF8.GetBytes(input);
        var actual = result.AsSpan().ToArray();
        
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetBytesPooled_UnicodeString_ReturnsCorrectBytes()
    {
        const string input = "你好世界 🌍";
        using var result = ArrayPoolHelper.GetBytesPooled(input);
        
        var expected = Encoding.UTF8.GetBytes(input);
        var actual = result.AsSpan().ToArray();
        
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetBytesPooled_LargeString_ReturnsCorrectBytes()
    {
        var input = new string('A', 10000);
        using var result = ArrayPoolHelper.GetBytesPooled(input);
        
        var expected = Encoding.UTF8.GetBytes(input);
        var actual = result.AsSpan().ToArray();
        
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetBytesPooled_Dispose_ReturnsBufferToPool()
    {
        // This test verifies that Dispose doesn't throw and buffer is properly returned
        const string input = "Test String";
        
        for (int i = 0; i < 100; i++)
        {
            using var result = ArrayPoolHelper.GetBytesPooled(input);
            // Buffer should be returned to pool on dispose
        }
        
        // No assertion - test passes if no exceptions thrown
    }

    #endregion

    #region GetStringFast Tests

    [Fact]
    public void GetStringFast_EmptySpan_ReturnsEmptyString()
    {
        var result = ArrayPoolHelper.GetStringFast(ReadOnlySpan<byte>.Empty);
        
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetStringFast_AsciiBytes_ReturnsCorrectString()
    {
        var bytes = Encoding.UTF8.GetBytes("Hello World");
        var result = ArrayPoolHelper.GetStringFast(bytes);
        
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void GetStringFast_UnicodeBytes_ReturnsCorrectString()
    {
        var bytes = Encoding.UTF8.GetBytes("你好世界 🌍");
        var result = ArrayPoolHelper.GetStringFast(bytes);
        
        Assert.Equal("你好世界 🌍", result);
    }

    #endregion

    #region ToBase64StringPooled Tests

    [Fact]
    public void ToBase64StringPooled_EmptySpan_ReturnsEmptyString()
    {
        var result = ArrayPoolHelper.ToBase64StringPooled(ReadOnlySpan<byte>.Empty);
        
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ToBase64StringPooled_SmallData_ReturnsCorrectBase64()
    {
        var input = Encoding.UTF8.GetBytes("Hello");
        var result = ArrayPoolHelper.ToBase64StringPooled(input);
        
        var expected = Convert.ToBase64String(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToBase64StringPooled_LargeData_ReturnsCorrectBase64()
    {
        var input = new byte[1000];
        Random.Shared.NextBytes(input);
        
        var result = ArrayPoolHelper.ToBase64StringPooled(input);
        
        var expected = Convert.ToBase64String(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToBase64StringPooled_UnicodeData_ReturnsCorrectBase64()
    {
        var input = Encoding.UTF8.GetBytes("你好世界 🌍");
        var result = ArrayPoolHelper.ToBase64StringPooled(input);
        
        var expected = Convert.ToBase64String(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region FromBase64StringPooled Tests

    [Fact]
    public void FromBase64StringPooled_EmptyString_ReturnsEmptyArray()
    {
        using var result = ArrayPoolHelper.FromBase64StringPooled("");
        
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void FromBase64StringPooled_NullString_ReturnsEmptyArray()
    {
        using var result = ArrayPoolHelper.FromBase64StringPooled(null!);
        
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void FromBase64StringPooled_ValidBase64_ReturnsCorrectBytes()
    {
        var original = Encoding.UTF8.GetBytes("Hello World");
        var base64 = Convert.ToBase64String(original);
        
        using var result = ArrayPoolHelper.FromBase64StringPooled(base64);
        
        Assert.Equal(original, result.AsSpan().ToArray());
    }

    [Fact]
    public void FromBase64StringPooled_LargeBase64_ReturnsCorrectBytes()
    {
        var original = new byte[1000];
        Random.Shared.NextBytes(original);
        var base64 = Convert.ToBase64String(original);
        
        using var result = ArrayPoolHelper.FromBase64StringPooled(base64);
        
        Assert.Equal(original, result.AsSpan().ToArray());
    }

    [Fact]
    public void FromBase64StringPooled_InvalidBase64_ThrowsException()
    {
        Assert.Throws<FormatException>(() =>
        {
            using var result = ArrayPoolHelper.FromBase64StringPooled("Invalid!@#$");
        });
    }

    [Fact]
    public void FromBase64StringPooled_Dispose_ReturnsBufferToPool()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Test"));
        
        for (int i = 0; i < 100; i++)
        {
            using var result = ArrayPoolHelper.FromBase64StringPooled(base64);
            // Buffer should be returned to pool on dispose
        }
        
        // No assertion - test passes if no exceptions thrown
    }

    #endregion

    #region TryToBase64Chars Tests

    [Fact]
    public void TryToBase64Chars_SufficientBuffer_ReturnsTrue()
    {
        var input = Encoding.UTF8.GetBytes("Hello");
        Span<char> buffer = stackalloc char[100];
        
        var success = ArrayPoolHelper.TryToBase64Chars(input, buffer, out int written);
        
        Assert.True(success);
        Assert.True(written > 0);
    }

    [Fact]
    public void TryToBase64Chars_InsufficientBuffer_ReturnsFalse()
    {
        var input = Encoding.UTF8.GetBytes("Hello World");
        Span<char> buffer = stackalloc char[2]; // Too small
        
        var success = ArrayPoolHelper.TryToBase64Chars(input, buffer, out int written);
        
        Assert.False(success);
    }

    #endregion

    #region TryFromBase64String Tests

    [Fact]
    public void TryFromBase64String_SufficientBuffer_ReturnsTrue()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello"));
        Span<byte> buffer = stackalloc byte[100];
        
        var success = ArrayPoolHelper.TryFromBase64String(base64, buffer, out int written);
        
        Assert.True(success);
        Assert.True(written > 0);
    }

    [Fact]
    public void TryFromBase64String_InsufficientBuffer_ReturnsFalse()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello World"));
        Span<byte> buffer = stackalloc byte[2]; // Too small
        
        var success = ArrayPoolHelper.TryFromBase64String(base64, buffer, out int written);
        
        Assert.False(success);
    }

    #endregion

    #region Round-trip Tests

    [Fact]
    public void RoundTrip_GetBytesPooled_GetStringFast()
    {
        const string original = "Hello World 你好 🌍";
        
        using var bytes = ArrayPoolHelper.GetBytesPooled(original);
        var result = ArrayPoolHelper.GetStringFast(bytes.AsSpan());
        
        Assert.Equal(original, result);
    }

    [Fact]
    public void RoundTrip_ToBase64StringPooled_FromBase64StringPooled()
    {
        var original = Encoding.UTF8.GetBytes("Test Data 测试数据");
        
        var base64 = ArrayPoolHelper.ToBase64StringPooled(original);
        using var decoded = ArrayPoolHelper.FromBase64StringPooled(base64);
        
        Assert.Equal(original, decoded.AsSpan().ToArray());
    }

    #endregion

    #region Memory Safety Tests

    [Fact]
    public void MemorySafety_MultipleDispose_DoesNotThrow()
    {
        var rentedArray = ArrayPoolHelper.GetBytesPooled("Test");
        
        rentedArray.Dispose();
        rentedArray.Dispose(); // Should not throw
        
        // No assertion - test passes if no exceptions
    }

    [Fact]
    public async System.Threading.Tasks.Task MemorySafety_ConcurrentAccess_NoCorruption()
    {
        const int iterations = 1000;
        var tasks = new System.Threading.Tasks.Task[10];
        
        for (int i = 0; i < tasks.Length; i++)
        {
            int threadId = i;
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    using var bytes = ArrayPoolHelper.GetBytesPooled($"Thread{threadId}-{j}");
                    var str = ArrayPoolHelper.GetStringFast(bytes.AsSpan());
                    Assert.Equal($"Thread{threadId}-{j}", str);
                }
            });
        }
        
        await System.Threading.Tasks.Task.WhenAll(tasks);
    }

    #endregion
}

