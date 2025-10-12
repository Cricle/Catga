namespace Catga.AspNetCore;

/// <summary>Catga ASP.NET Core integration options (immutable record)</summary>
public record CatgaAspNetCoreOptions
{
    public bool EnableDashboard { get; init; } = true;
    public string DashboardPathPrefix { get; init; } = "/catga";
    public bool AutoMapHealthEndpoints { get; init; } = true;
    public ErrorResponseFormat ErrorFormat { get; init; } = ErrorResponseFormat.Simple;
}

public enum ErrorResponseFormat
{
    Simple,
    ProblemDetails
}

