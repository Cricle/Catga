using Catga;
using Catga.Observability;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

/// <summary>
/// Unit tests for CatgaTelemetry.
/// </summary>
public class CatgaTelemetryTests
{
    [Fact]
    public void DefaultSource_IsNotNull()
    {
        // Assert
        CatgaTelemetry.DefaultSource.Should().NotBeNull();
    }

    [Fact]
    public void DefaultMeter_IsNotNull()
    {
        // Assert
        CatgaTelemetry.DefaultMeter.Should().NotBeNull();
    }

    [Fact]
    public void CatgaActivitySource_Source_IsNotNull()
    {
        // Assert
        CatgaActivitySource.Source.Should().NotBeNull();
    }

    [Fact]
    public void CatgaActivitySource_Name_IsNotEmpty()
    {
        // Assert
        CatgaActivitySource.Source.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CatgaDiagnostics_Meter_IsNotNull()
    {
        // Assert
        CatgaDiagnostics.Meter.Should().NotBeNull();
    }

    [Fact]
    public void CatgaDiagnostics_MeterName_IsNotEmpty()
    {
        // Assert
        CatgaDiagnostics.Meter.Name.Should().NotBeNullOrEmpty();
    }
}
