using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

public class VersionInfoTests
{
    [Fact]
    public void VersionInfo_CanBeCreated()
    {
        var info = new VersionInfo();
        info.Should().NotBeNull();
    }

    [Fact]
    public void VersionInfo_CanSetVersion()
    {
        var info = new VersionInfo { Version = 42 };
        info.Version.Should().Be(42);
    }

    [Fact]
    public void VersionInfo_CanSetTimestamp()
    {
        var timestamp = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var info = new VersionInfo { Timestamp = timestamp };
        info.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void VersionInfo_CanSetEventType()
    {
        var info = new VersionInfo { EventType = "OrderPlaced" };
        info.EventType.Should().Be("OrderPlaced");
    }

    [Fact]
    public void VersionInfo_FullyPopulated()
    {
        var timestamp = DateTime.UtcNow;
        var info = new VersionInfo
        {
            Version = 100,
            Timestamp = timestamp,
            EventType = "PaymentProcessed"
        };

        info.Version.Should().Be(100);
        info.Timestamp.Should().Be(timestamp);
        info.EventType.Should().Be("PaymentProcessed");
    }
}
