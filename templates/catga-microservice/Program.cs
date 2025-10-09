using Catga;
using Catga.DistributedId;
using CatgaMicroservice;

var builder = WebApplication.CreateBuilder(args);

// Add Catga
builder.Services.AddCatgaMediator(options =>
{
    options.ScanHandlers = true;
    options.EnableSourceGenerator = true;
});

// Add distributed ID
builder.Services.AddSnowflakeId(options =>
{
    options.WorkerId = builder.Configuration.GetValue<int>("DistributedId:WorkerId");
    options.DataCenterId = builder.Configuration.GetValue<int>("DistributedId:DataCenterId");
});

// Add resilience
builder.Services.AddCircuitBreaker(options =>
{
    options.FailureThreshold = 5;
    options.SuccessThreshold = 2;
    options.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddConcurrencyLimiter(options =>
{
    options.MaxConcurrency = 500;
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceHealthCheck>("service");

// Add observability
builder.Services.AddSingleton<CatgaMetrics>();

#if (UsePrometheus)
// Add Prometheus metrics
builder.Services.AddPrometheusMetrics();
#endif

// Add endpoints
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "CatgaMicroservice API", 
        Version = "v1",
        Description = "A microservice built with Catga CQRS framework"
    });
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");

#if (UsePrometheus)
// Metrics endpoint
app.MapPrometheusMetrics("/metrics");
#endif

// API endpoints
app.MapControllers();

// Sample endpoint
app.MapGet("/", () => new
{
    service = "CatgaMicroservice",
    version = "1.0.0",
    status = "running",
    timestamp = DateTime.UtcNow
});

app.Run();

