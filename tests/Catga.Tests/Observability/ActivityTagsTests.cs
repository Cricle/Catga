using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

public class ActivityTagsTests
{
    [Fact]
    public void Tags_MessageType_HasCorrectValue()
    {
        CatgaActivitySource.Tags.MessageType.Should().Be("catga.message.type");
    }

    [Fact]
    public void Tags_MessageId_HasCorrectValue()
    {
        CatgaActivitySource.Tags.MessageId.Should().Be("catga.message.id");
    }

    [Fact]
    public void Tags_QoS_HasCorrectValue()
    {
        CatgaActivitySource.Tags.QoS.Should().Be("catga.qos");
    }

    [Fact]
    public void Tags_MessagingSystem_HasCorrectValue()
    {
        CatgaActivitySource.Tags.MessagingSystem.Should().Be("messaging.system");
    }

    [Fact]
    public void Tags_MessagingDestination_HasCorrectValue()
    {
        CatgaActivitySource.Tags.MessagingDestination.Should().Be("messaging.destination.name");
    }

    [Fact]
    public void Tags_MessagingOperation_HasCorrectValue()
    {
        CatgaActivitySource.Tags.MessagingOperation.Should().Be("messaging.operation");
    }

    [Fact]
    public void SourceName_IsNotNullOrEmpty()
    {
        CatgaActivitySource.SourceName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Version_IsNotNullOrEmpty()
    {
        CatgaActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Source_IsNotNull()
    {
        CatgaActivitySource.Source.Should().NotBeNull();
    }

    [Fact]
    public void Source_Name_MatchesSourceName()
    {
        CatgaActivitySource.Source.Name.Should().Be(CatgaActivitySource.SourceName);
    }
}
