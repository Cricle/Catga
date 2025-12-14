using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

public class TypeNameCacheExtendedTests
{
    private class TestClass { }
    private record TestRecord(string Value);
    private struct TestStruct { }

    [Fact]
    public void FullName_ForClass_ReturnsFullTypeName()
    {
        var name = TypeNameCache<TestClass>.FullName;
        name.Should().Contain("TestClass");
    }

    [Fact]
    public void FullName_ForRecord_ReturnsFullTypeName()
    {
        var name = TypeNameCache<TestRecord>.FullName;
        name.Should().Contain("TestRecord");
    }

    [Fact]
    public void FullName_ForStruct_ReturnsFullTypeName()
    {
        var name = TypeNameCache<TestStruct>.FullName;
        name.Should().Contain("TestStruct");
    }

    [Fact]
    public void FullName_ForPrimitiveInt_ReturnsCorrectName()
    {
        var name = TypeNameCache<int>.FullName;
        name.Should().Contain("Int32");
    }

    [Fact]
    public void FullName_ForPrimitiveString_ReturnsCorrectName()
    {
        var name = TypeNameCache<string>.FullName;
        name.Should().Contain("String");
    }

    [Fact]
    public void FullName_IsCached_ReturnsSameInstance()
    {
        var name1 = TypeNameCache<TestClass>.FullName;
        var name2 = TypeNameCache<TestClass>.FullName;

        ReferenceEquals(name1, name2).Should().BeTrue();
    }

    [Fact]
    public void FullName_ForGeneric_ContainsGenericTypeName()
    {
        var name = TypeNameCache<List<string>>.FullName;
        name.Should().Contain("List");
    }

    [Fact]
    public void FullName_ForNestedGeneric_ContainsAllTypeNames()
    {
        var name = TypeNameCache<Dictionary<string, List<int>>>.FullName;
        name.Should().Contain("Dictionary");
    }
}
