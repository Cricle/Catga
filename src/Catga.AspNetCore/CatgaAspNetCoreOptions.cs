namespace Catga.AspNetCore;

/// <summary>Catga ASP.NET Core integration options</summary>
public class CatgaAspNetCoreOptions
{
    public bool EnableDashboard { get; set; } = true;
    public string DashboardPathPrefix { get; set; } = "/catga";
    public bool AutoMapHealthEndpoints { get; set; } = true;
    public ErrorResponseFormat ErrorFormat { get; set; } = ErrorResponseFormat.Simple;
}

public enum ErrorResponseFormat
{
    Simple,
    ProblemDetails
}

