using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for ExceptionTypeCache - zero-allocation exception type name caching
/// </summary>
public class ExceptionTypeCacheTests
{
    [Fact]
    public void GetTypeName_ShouldReturnCorrectTypeName()
    {
        // Arrange
        var ex = new InvalidOperationException("test");

        // Act
        var name = ExceptionTypeCache.GetTypeName(ex);

        // Assert
        name.Should().Be("InvalidOperationException");
    }

    [Fact]
    public void GetTypeName_ShouldCacheResult()
    {
        // Arrange
        var ex1 = new ArgumentNullException("param1");
        var ex2 = new ArgumentNullException("param2");

        // Act
        var name1 = ExceptionTypeCache.GetTypeName(ex1);
        var name2 = ExceptionTypeCache.GetTypeName(ex2);

        // Assert
        name1.Should().Be("ArgumentNullException");
        name2.Should().Be("ArgumentNullException");
        ReferenceEquals(name1, name2).Should().BeTrue("cached strings should be the same instance");
    }

    [Fact]
    public void GetFullTypeName_ShouldReturnCorrectFullName()
    {
        // Arrange
        var ex = new InvalidOperationException("test");

        // Act
        var fullName = ExceptionTypeCache.GetFullTypeName(ex);

        // Assert
        fullName.Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public void GetFullTypeName_ShouldCacheResult()
    {
        // Arrange
        var ex1 = new ArgumentException("msg1");
        var ex2 = new ArgumentException("msg2");

        // Act
        var fullName1 = ExceptionTypeCache.GetFullTypeName(ex1);
        var fullName2 = ExceptionTypeCache.GetFullTypeName(ex2);

        // Assert
        fullName1.Should().Be("System.ArgumentException");
        fullName2.Should().Be("System.ArgumentException");
        ReferenceEquals(fullName1, fullName2).Should().BeTrue("cached strings should be the same instance");
    }

    [Fact]
    public void GetTypeName_DifferentExceptionTypes_ShouldReturnDifferentNames()
    {
        // Arrange
        var ex1 = new InvalidOperationException();
        var ex2 = new ArgumentNullException();

        // Act
        var name1 = ExceptionTypeCache.GetTypeName(ex1);
        var name2 = ExceptionTypeCache.GetTypeName(ex2);

        // Assert
        name1.Should().NotBe(name2);
        name1.Should().Be("InvalidOperationException");
        name2.Should().Be("ArgumentNullException");
    }

    [Fact]
    public void GetTypeName_CustomException_ShouldWork()
    {
        // Arrange
        var ex = new CustomTestException("test");

        // Act
        var name = ExceptionTypeCache.GetTypeName(ex);
        var fullName = ExceptionTypeCache.GetFullTypeName(ex);

        // Assert
        name.Should().Be("CustomTestException");
        fullName.Should().Contain("CustomTestException");
    }

    [Fact]
    public void GetTypeName_IsThreadSafe()
    {
        // Arrange & Act
        var results = new string[100];
        Parallel.For(0, 100, i =>
        {
            var ex = new InvalidOperationException($"test-{i}");
            results[i] = ExceptionTypeCache.GetTypeName(ex);
        });

        // Assert
        results.Should().AllBe("InvalidOperationException");
    }

    private class CustomTestException : Exception
    {
        public CustomTestException(string message) : base(message) { }
    }
}
