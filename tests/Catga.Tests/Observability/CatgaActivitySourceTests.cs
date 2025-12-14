using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

public class CatgaActivitySourceTests
{
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

    [Fact]
    public void Source_IsNotNull()
    {
        CatgaActivitySource.Source.Should().NotBeNull();
    }

    [Fact]
    public void Source_HasCorrectName()
    {
        CatgaActivitySource.Source.Name.Should().Be(CatgaActivitySource.SourceName);
    }

    [Fact]
    public void Tags_MessagingSystem_HasValue()
    {
        CatgaActivitySource.Tags.MessagingSystem.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_MessagingDestination_HasValue()
    {
        CatgaActivitySource.Tags.MessagingDestination.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_MessagingOperation_HasValue()
    {
        CatgaActivitySource.Tags.MessagingOperation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_MessageType_HasValue()
    {
        CatgaActivitySource.Tags.MessageType.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_MessageId_HasValue()
    {
        CatgaActivitySource.Tags.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_QoS_HasValue()
    {
        CatgaActivitySource.Tags.QoS.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Tags_AreConstants()
    {
        var tag1 = CatgaActivitySource.Tags.MessagingSystem;
        var tag2 = CatgaActivitySource.Tags.MessagingSystem;

        ReferenceEquals(tag1, tag2).Should().BeTrue();
    }
}
