using Catga.Observability;
using FluentAssertions;
using System.Diagnostics;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive scenario tests for Catga diagnostics and observability
/// </summary>
public class DiagnosticsScenarioTests
{
    #region ActivitySource Tests

    [Fact]
    public void ActivitySource_IsNotNull()
    {
        CatgaActivitySource.Source.Should().NotBeNull();
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        CatgaActivitySource.Source.Name.Should().Be(CatgaActivitySource.SourceName);
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        CatgaActivitySource.Source.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SourceName_IsNotEmpty()
    {
        CatgaActivitySource.SourceName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Version_IsNotEmpty()
    {
        CatgaActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Tags Tests

    [Fact]
    public void Tags_AllDefined()
    {
        CatgaActivitySource.Tags.MessageType.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessageId.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.QoS.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessagingSystem.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessagingDestination.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessagingOperation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_FollowNamingConvention()
    {
        // OpenTelemetry convention uses dot notation
        CatgaActivitySource.Tags.MessagingSystem.Should().Contain(".");
        CatgaActivitySource.Tags.MessagingDestination.Should().Contain(".");
        CatgaActivitySource.Tags.MessagingOperation.Should().Contain(".");
    }

    [Fact]
    public void Tags_CatgaTagsHavePrefix()
    {
        CatgaActivitySource.Tags.MessageType.Should().StartWith("catga.");
        CatgaActivitySource.Tags.MessageId.Should().StartWith("catga.");
        CatgaActivitySource.Tags.QoS.Should().StartWith("catga.");
    }

    [Fact]
    public void Tags_AreUnique()
    {
        var tags = new[]
        {
            CatgaActivitySource.Tags.MessageType,
            CatgaActivitySource.Tags.MessageId,
            CatgaActivitySource.Tags.QoS,
            CatgaActivitySource.Tags.MessagingSystem,
            CatgaActivitySource.Tags.MessagingDestination,
            CatgaActivitySource.Tags.MessagingOperation
        };

        tags.Distinct().Count().Should().Be(tags.Length);
    }

    #endregion

    #region Activity Creation Tests

    [Fact]
    public void StartActivity_WithoutListener_ReturnsNull()
    {
        // Without a listener, activity will be null
        var activity = CatgaActivitySource.Source.StartActivity("test-operation");

        // This is expected behavior when no listener is registered
        activity.Should().BeNull();
    }

    [Fact]
    public void Source_CanBeUsedForMultipleOperations()
    {
        var activity1 = CatgaActivitySource.Source.StartActivity("op1");
        var activity2 = CatgaActivitySource.Source.StartActivity("op2");
        var activity3 = CatgaActivitySource.Source.StartActivity("op3");

        // All should be null without listener, but the calls should not throw
        (activity1 == null && activity2 == null && activity3 == null).Should().BeTrue();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void ActivitySource_ConcurrentAccess_NoExceptions()
    {
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                var source = CatgaActivitySource.Source;
                var name = CatgaActivitySource.SourceName;
                var version = CatgaActivitySource.Version;
                _ = CatgaActivitySource.Tags.MessageType;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion
}
