using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// TypeNameCache unit tests - zero-allocation type name caching
/// </summary>
public class TypeNameCacheTests
{
    [Fact]
    public void TypeNameCache_Name_ShouldReturnCorrectTypeName()
    {
        // Act
        var name1 = TypeNameCache<string>.Name;
        var name2 = TypeNameCache<string>.Name;

        // Assert
        name1.Should().Be("String");
        name2.Should().Be("String"); // Same instance (cached)
        ReferenceEquals(name1, name2).Should().BeTrue();
    }

    [Fact]
    public void TypeNameCache_FullName_ShouldReturnCorrectFullName()
    {
        // Act
        var fullName1 = TypeNameCache<string>.FullName;
        var fullName2 = TypeNameCache<string>.FullName;

        // Assert
        fullName1.Should().Be("System.String");
        fullName2.Should().Be("System.String");
        ReferenceEquals(fullName1, fullName2).Should().BeTrue();
    }

    [Fact]
    public void TypeNameCache_DifferentTypes_ShouldReturnDifferentNames()
    {
        // Act
        var stringName = TypeNameCache<string>.Name;
        var intName = TypeNameCache<int>.Name;

        // Assert
        stringName.Should().NotBe(intName);
        stringName.Should().Be("String");
        intName.Should().Be("Int32");
    }

    [Fact]
    public void TypeNameCache_CustomType_ShouldWork()
    {
        // Act
        var name = TypeNameCache<TestCustomType>.Name;
        var fullName = TypeNameCache<TestCustomType>.FullName;

        // Assert
        name.Should().Be("TestCustomType");
        fullName.Should().Contain("TestCustomType");
    }

    [Fact]
    public void TypeNameCache_GenericType_ShouldWork()
    {
        // Act
        var name = TypeNameCache<List<string>>.Name;

        // Assert
        name.Should().Contain("List");
    }

    [Fact]
    public void ExceptionTypeCache_GetTypeName_ShouldCacheResults()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Test");
        var exception2 = new InvalidOperationException("Test2");

        // Act
        var name1 = ExceptionTypeCache.GetTypeName(exception1);
        var name2 = ExceptionTypeCache.GetTypeName(exception2);

        // Assert
        name1.Should().Be("InvalidOperationException");
        name2.Should().Be("InvalidOperationException");
        ReferenceEquals(name1, name2).Should().BeTrue(); // Cached
    }

    [Fact]
    public void ExceptionTypeCache_GetFullTypeName_ShouldCacheResults()
    {
        // Arrange
        var exception1 = new InvalidOperationException();
        var exception2 = new InvalidOperationException();

        // Act
        var fullName1 = ExceptionTypeCache.GetFullTypeName(exception1);
        var fullName2 = ExceptionTypeCache.GetFullTypeName(exception2);

        // Assert
        fullName1.Should().Be("System.InvalidOperationException");
        fullName2.Should().Be("System.InvalidOperationException");
        ReferenceEquals(fullName1, fullName2).Should().BeTrue(); // Cached
    }

    [Fact]
    public void ExceptionTypeCache_DifferentExceptionTypes_ShouldReturnDifferentNames()
    {
        // Arrange
        var exception1 = new InvalidOperationException();
        var exception2 = new ArgumentNullException();

        // Act
        var name1 = ExceptionTypeCache.GetTypeName(exception1);
        var name2 = ExceptionTypeCache.GetTypeName(exception2);

        // Assert
        name1.Should().Be("InvalidOperationException");
        name2.Should().Be("ArgumentNullException");
        name1.Should().NotBe(name2);
    }

    private class TestCustomType { }
}

