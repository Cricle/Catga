using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for CatgaResult factory methods
/// </summary>
public class CatgaResultFactoryTests
{
    #region Success Tests

    [Fact]
    public void Success_ReturnsSuccessResult()
    {
        var result = CatgaResult.Success();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SuccessT_WithValue_ReturnsSuccessWithValue()
    {
        var result = CatgaResult<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void SuccessT_WithString_ReturnsSuccessWithString()
    {
        var result = CatgaResult<string>.Success("hello");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void SuccessT_WithComplexType_ReturnsSuccess()
    {
        var data = new TestData { Id = 1, Name = "Test" };
        var result = CatgaResult<TestData>.Success(data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(1);
        result.Value.Name.Should().Be("Test");
    }

    #endregion

    #region Failure Tests

    [Fact]
    public void Failure_WithMessage_ReturnsFailure()
    {
        var result = CatgaResult.Failure("Error occurred");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void FailureT_WithMessage_ReturnsFailure()
    {
        var result = CatgaResult<int>.Failure("Error occurred");

        result.IsSuccess.Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SuccessT_WithNull_IsValid()
    {
        var result = CatgaResult<string?>.Success(null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void SuccessT_WithDefaultValue_IsValid()
    {
        var result = CatgaResult<int>.Success(default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    #endregion

    #region Concurrent Tests

    [Fact]
    public void Success_ConcurrentCreation_AllValid()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<CatgaResult>();

        Parallel.For(0, 100, _ =>
        {
            results.Add(CatgaResult.Success());
        });

        results.Count.Should().Be(100);
        results.All(r => r.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public void SuccessT_ConcurrentCreation_AllValid()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<CatgaResult<int>>();

        Parallel.For(0, 100, i =>
        {
            results.Add(CatgaResult<int>.Success(i));
        });

        results.Count.Should().Be(100);
        results.All(r => r.IsSuccess).Should().BeTrue();
        results.Select(r => r.Value).Distinct().Count().Should().Be(100);
    }

    #endregion

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
