namespace Catga.AspNetCore;

/// <summary>
/// Marks a method as a Catga endpoint handler.
/// The source generator will create a RegisterEndpoints method that maps this method to an HTTP endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class CatgaEndpointAttribute : Attribute
{
    /// <summary>
    /// HTTP method (Post, Get, Put, Delete, Patch)
    /// </summary>
    public string HttpMethod { get; }

    /// <summary>
    /// Route pattern (e.g., "/api/orders/{id}")
    /// </summary>
    public string Route { get; }

    /// <summary>
    /// Optional endpoint name for OpenAPI/Swagger
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional endpoint description for OpenAPI/Swagger
    /// </summary>
    public string? Description { get; set; }

    public CatgaEndpointAttribute(string httpMethod, string route)
    {
        HttpMethod = httpMethod;
        Route = route;
    }
}
