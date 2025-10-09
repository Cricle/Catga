using Catga;
using Catga.DistributedId;
#if (UseNats)
using Catga.Nats;
#endif
#if (UseRedis)
using StackExchange.Redis;
#endif

var builder = WebApplication.CreateBuilder(args);

// Add Catga CQRS
builder.Services.AddCatgaMediator(options =>
{
    options.ScanHandlers = true;
    options.EnableSourceGenerator = true;
});

#if (UseDistributedId)
// Add Distributed ID
builder.Services.AddSnowflakeId(options =>
{
    options.WorkerId = builder.Configuration.GetValue<int>("DistributedId:WorkerId");
    options.DataCenterId = builder.Configuration.GetValue<int>("DistributedId:DataCenterId");
    options.Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
});
#endif

#if (UseNats)
// Add NATS messaging
builder.Services.AddNatsTransport(options =>
{
    options.Url = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
    options.EnableJetStream = true;
});
#endif

#if (UseRedis)
// Add Redis
var redisConnection = ConnectionMultiplexer.Connect(
    builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);
#endif

#if (UseOutbox)
// Add Outbox pattern
builder.Services.AddOutboxPattern(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.BatchSize = 100;
});
#endif

// Add resilience
builder.Services.AddCircuitBreaker(options =>
{
    options.FailureThreshold = 5;
    options.SuccessThreshold = 2;
    options.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddConcurrencyLimiter(options =>
{
    options.MaxConcurrency = 1000;
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<CatgaHealthCheck>("catga")
#if (UseRedis)
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379")
#endif
#if (UseNats)
    .AddCheck<NatsHealthCheck>("nats")
#endif
    ;

// Add observability
builder.Services.AddSingleton<CatgaMetrics>();

// Add controllers and endpoints
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

// Metrics endpoint
app.MapGet("/metrics", (CatgaMetrics metrics) =>
{
    var snapshot = metrics.GetSnapshot();
    return Results.Ok(snapshot);
});

// Sample endpoints
app.MapPost("/api/orders", async (
    ICatgaMediator mediator,
    CreateOrderCommand command) =>
{
    var result = await mediator.SendAsync(command);
    return result.IsSuccess 
        ? Results.Created($"/api/orders/{result.Value.OrderId}", result.Value)
        : Results.BadRequest(result.Error);
});

#if (UseDistributedId)
app.MapGet("/api/id", (ISnowflakeIdGenerator idGen) =>
{
    return Results.Ok(new { id = idGen.NextId() });
});
#endif

app.MapControllers();

app.Run();

