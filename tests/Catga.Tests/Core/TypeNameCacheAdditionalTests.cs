using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

public class TypeNameCacheAdditionalTests
{
    private record TestRecord(string Value);
    private class TestClass { public string? Data { get; set; } }
    private struct TestStruct { public int Value = 0; public TestStruct() { } }

    [Fact]
    public void Name_ForRecord_ReturnsTypeName()
    {
        TypeNameCache<TestRecord>.Name.Should().Be("TestRecord");
    }

    [Fact]
    public void Name_ForClass_ReturnsTypeName()
    {
        TypeNameCache<TestClass>.Name.Should().Be("TestClass");
    }

    [Fact]
    public void Name_ForStruct_ReturnsTypeName()
    {
        TypeNameCache<TestStruct>.Name.Should().Be("TestStruct");
    }

    [Fact]
    public void FullName_ForRecord_ContainsNamespace()
    {
        TypeNameCache<TestRecord>.FullName.Should().Contain("TypeNameCacheAdditionalTests");
        TypeNameCache<TestRecord>.FullName.Should().Contain("TestRecord");
    }

    [Fact]
    public void Name_IsCached()
    {
        var name1 = TypeNameCache<TestRecord>.Name;
        var name2 = TypeNameCache<TestRecord>.Name;

        ReferenceEquals(name1, name2).Should().BeTrue();
    }

    [Fact]
    public void FullName_IsCached()
    {
        var name1 = TypeNameCache<TestRecord>.FullName;
        var name2 = TypeNameCache<TestRecord>.FullName;

        ReferenceEquals(name1, name2).Should().BeTrue();
    }

    [Fact]
    public void Name_ForGenericType_HandlesCorrectly()
    {
        TypeNameCache<List<string>>.Name.Should().Contain("List");
    }

    [Fact]
    public void Name_ForBuiltInType_ReturnsTypeName()
    {
        TypeNameCache<int>.Name.Should().Be("Int32");
        TypeNameCache<string>.Name.Should().Be("String");
        TypeNameCache<bool>.Name.Should().Be("Boolean");
    }
}
