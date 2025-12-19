using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga;

/// <summary>Public telemetry sources for auto-generated handlers.</summary>
public static class CatgaTelemetry
{
    public static readonly ActivitySource DefaultSource = Catga.Observability.CatgaActivitySource.Source;
    public static readonly Meter DefaultMeter = Catga.Observability.CatgaDiagnostics.Meter;
}
