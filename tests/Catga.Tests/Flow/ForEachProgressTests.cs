using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class ForEachProgressTests
{
    [Fact]
    public void ForEachProgress_DefaultValues()
    {
        var progress = new ForEachProgress();

        progress.CurrentIndex.Should().Be(0);
        progress.TotalCount.Should().Be(0);
        progress.CompletedIndices.Should().BeEmpty();
        progress.FailedIndices.Should().BeEmpty();
    }

    [Fact]
    public void ForEachProgress_CanSetCurrentIndex()
    {
        var progress = new ForEachProgress { CurrentIndex = 5 };
        progress.CurrentIndex.Should().Be(5);
    }

    [Fact]
    public void ForEachProgress_CanSetTotalCount()
    {
        var progress = new ForEachProgress { TotalCount = 100 };
        progress.TotalCount.Should().Be(100);
    }

    [Fact]
    public void ForEachProgress_CanTrackCompletedIndices()
    {
        var progress = new ForEachProgress
        {
            CompletedIndices = new List<int> { 0, 1, 2, 3, 4 }
        };

        progress.CompletedIndices.Should().HaveCount(5);
        progress.CompletedIndices.Should().Contain(new[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public void ForEachProgress_CanTrackFailedIndices()
    {
        var progress = new ForEachProgress
        {
            FailedIndices = new List<int> { 5, 10 }
        };

        progress.FailedIndices.Should().HaveCount(2);
        progress.FailedIndices.Should().Contain(new[] { 5, 10 });
    }

    [Fact]
    public void ForEachProgress_CalculateCompletionPercentage()
    {
        var progress = new ForEachProgress
        {
            TotalCount = 100,
            CompletedIndices = Enumerable.Range(0, 50).ToList()
        };

        var percentage = (double)progress.CompletedIndices.Count / progress.TotalCount * 100;
        percentage.Should().Be(50);
    }

    [Fact]
    public void ForEachProgress_AllCompleted()
    {
        var progress = new ForEachProgress
        {
            TotalCount = 10,
            CurrentIndex = 10,
            CompletedIndices = Enumerable.Range(0, 10).ToList()
        };

        progress.CompletedIndices.Count.Should().Be(progress.TotalCount);
    }
}
