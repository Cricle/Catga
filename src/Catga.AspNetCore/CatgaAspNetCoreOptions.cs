namespace Catga.AspNetCore;

/// <summary>
/// Options for Catga ASP.NET Core integration
/// </summary>
public class CatgaAspNetCoreOptions
{
    /// <summary>
    /// Enable Catga diagnostics dashboard
    /// Default: true
    /// </summary>
    public bool EnableDashboard { get; set; } = true;

    /// <summary>
    /// Dashboard path prefix
    /// Default: /catga
    /// </summary>
    public string DashboardPathPrefix { get; set; } = "/catga";

    /// <summary>
    /// Auto map health endpoints
    /// Default: true
    /// </summary>
    public bool AutoMapHealthEndpoints { get; set; } = true;

    /// <summary>
    /// Default response format for errors
    /// </summary>
    public ErrorResponseFormat ErrorFormat { get; set; } = ErrorResponseFormat.Simple;
}

public enum ErrorResponseFormat
{
    Simple,      // { error: "message" }
    ProblemDetails  // RFC 7807 format
}

