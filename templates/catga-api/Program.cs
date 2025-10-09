using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add Catga CQRS
builder.Services.AddCatga(options =>
{
    options.MaxConcurrentRequests = 100;
#if (EnableRateLimiting)
    options.EnableRateLimiting = true;
    options.RateLimitBurstCapacity = 100;
    options.RateLimitRequestsPerSecond = 50;
#endif
    options.EnableCircuitBreaker = true;
    options.CircuitBreakerFailureThreshold = 5;
});

// Register handlers from this assembly
builder.Services.AddCatgaHandlers();

#if (EnableDistributedId)
// Add Distributed ID
builder.Services.AddDistributedId(options =>
{
    options.WorkerId = 1; // Configure based on deployment
});
#endif

#if (EnableOpenAPI)
// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CatgaApi", Version = "v1" });
});
#endif

var app = builder.Build();

#if (EnableOpenAPI)
// Configure Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
#endif

app.UseHttpsRedirection();

// Map Catga endpoints
app.MapCatgaEndpoints();

// Sample endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithOpenApi();

app.Run();

