using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Thread safety tests for TypeNameCache
/// </summary>
public class TypeNameCacheThreadSafetyTests
{
    private class TestClass1 { }
    private class TestClass2 { }
    private class TestClass3 { }
    private record TestRecord(string Value);
    private struct TestStruct { }

    [Fact]
    public void TypeNameCache_ConcurrentAccess_SameType_ReturnsSameValue()
    {
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 100, _ =>
        {
            names.Add(TypeNameCache<TestClass1>.FullName);
        });

        names.Distinct().Count().Should().Be(1);
    }

    [Fact]
    public void TypeNameCache_ConcurrentAccess_DifferentTypes_AllValid()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<(Type type, string name)>();

        Parallel.Invoke(
            () => results.Add((typeof(TestClass1), TypeNameCache<TestClass1>.FullName)),
            () => results.Add((typeof(TestClass2), TypeNameCache<TestClass2>.FullName)),
            () => results.Add((typeof(TestClass3), TypeNameCache<TestClass3>.FullName)),
            () => results.Add((typeof(TestRecord), TypeNameCache<TestRecord>.FullName)),
            () => results.Add((typeof(TestStruct), TypeNameCache<TestStruct>.FullName))
        );

        results.Count.Should().Be(5);
        foreach (var (type, name) in results)
        {
            name.Should().Contain(type.Name);
        }
    }

    [Fact]
    public void TypeNameCache_HighConcurrency_NoExceptions()
    {
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 1000, i =>
        {
            try
            {
                _ = TypeNameCache<TestClass1>.FullName;
                _ = TypeNameCache<TestClass2>.FullName;
                _ = TypeNameCache<int>.FullName;
                _ = TypeNameCache<string>.FullName;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void TypeNameCache_GenericTypes_ConcurrentAccess()
    {
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 100, _ =>
        {
            names.Add(TypeNameCache<List<string>>.FullName);
            names.Add(TypeNameCache<Dictionary<string, int>>.FullName);
        });

        names.Count.Should().Be(200);
    }

    [Fact]
    public void TypeNameCache_ReturnsSameReference_Cached()
    {
        var name1 = TypeNameCache<TestClass1>.FullName;
        var name2 = TypeNameCache<TestClass1>.FullName;

        ReferenceEquals(name1, name2).Should().BeTrue();
    }
}
