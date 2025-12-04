using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga;

/// <summary>
/// Default telemetry sources for auto-generated handlers.
/// </summary>
public static class CatgaTelemetry
{
    /// <summary>Default ActivitySource for handlers.</summary>
    public static readonly ActivitySource DefaultSource = Catga.Observability.CatgaActivitySource.Source;

    /// <summary>Default Meter for handlers.</summary>
    public static readonly Meter DefaultMeter = Catga.Observability.CatgaDiagnostics.Meter;
}
