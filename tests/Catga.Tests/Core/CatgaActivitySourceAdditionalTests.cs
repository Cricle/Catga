using Catga.Observability;
using FluentAssertions;
using System.Diagnostics;

namespace Catga.Tests.Core;

public class CatgaActivitySourceAdditionalTests
{
    [Fact]
    public void ActivitySource_CreateActivity_ReturnsNullWhenNoListener()
    {
        var activity = CatgaActivitySource.Source.StartActivity("test-operation");

        // Without a listener, activity will be null
        // This is expected behavior
        activity.Should().BeNull();
    }

    [Fact]
    public void Tags_AllTagsAreDefined()
    {
        CatgaActivitySource.Tags.MessageType.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessageId.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.QoS.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessagingSystem.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessagingDestination.Should().NotBeNullOrEmpty();
        CatgaActivitySource.Tags.MessagingOperation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Source_Name_MatchesSourceName()
    {
        CatgaActivitySource.Source.Name.Should().Be(CatgaActivitySource.SourceName);
    }

    [Fact]
    public void Source_Version_MatchesVersion()
    {
        CatgaActivitySource.Source.Version.Should().Be(CatgaActivitySource.Version);
    }

    [Fact]
    public void Tags_MessagingSystem_IsValidFormat()
    {
        CatgaActivitySource.Tags.MessagingSystem.Should().Contain(".");
    }

    [Fact]
    public void Tags_MessagingDestination_IsValidFormat()
    {
        CatgaActivitySource.Tags.MessagingDestination.Should().Contain(".");
    }
}
