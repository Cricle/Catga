using FluentAssertions;

namespace Catga.Tests.SourceGenerator;

/// <summary>
/// Tests for AutoTelemetry attributes and expected behavior.
/// Full generator tests require Microsoft.CodeAnalysis.CSharp.Workspaces package.
/// </summary>
public class AutoTelemetryGeneratorTests
{
    [Fact]
    public void CatgaHandlerAttribute_HasExpectedProperties()
    {
        var attr = new CatgaHandlerAttribute();

        attr.Lifetime.Should().Be(HandlerLifetime.Scoped);
        attr.AutoRegister.Should().BeTrue();
        attr.ActivitySource.Should().BeNull();
        attr.Meter.Should().BeNull();
    }

    [Fact]
    public void CatgaHandlerAttribute_WithLifetime_SetsProperty()
    {
        var attr = new CatgaHandlerAttribute(HandlerLifetime.Singleton);

        attr.Lifetime.Should().Be(HandlerLifetime.Singleton);
    }

    [Fact]
    public void MetricAttribute_HasExpectedProperties()
    {
        var attr = new MetricAttribute("test.metric");

        attr.Name.Should().Be("test.metric");
        attr.Type.Should().Be(MetricType.Counter);
        attr.Unit.Should().BeNull();
        attr.Meter.Should().BeNull();
    }

    [Fact]
    public void MetricAttribute_WithHistogram_SetsType()
    {
        var attr = new MetricAttribute("test.duration")
        {
            Type = MetricType.Histogram,
            Unit = "ms"
        };

        attr.Type.Should().Be(MetricType.Histogram);
        attr.Unit.Should().Be("ms");
    }

    [Fact]
    public void NoTagAttribute_CanBeCreated()
    {
        var attr = new NoTagAttribute();
        attr.Should().NotBeNull();
    }

    [Fact]
    public void CatgaTelemetry_HasDefaultInstances()
    {
        CatgaTelemetry.DefaultSource.Should().NotBeNull();
        CatgaTelemetry.DefaultSource.Name.Should().Be("Catga");

        CatgaTelemetry.DefaultMeter.Should().NotBeNull();
        CatgaTelemetry.DefaultMeter.Name.Should().Be("Catga");
    }
}
