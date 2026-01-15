// Catga OrderSystem Example - Complete CQRS Demo
// Features: InMemory/Redis/NATS, Standalone/Cluster, Event Sourcing
// Usage: dotnet run -- [--transport inmemory|redis|nats] [--persistence inmemory|redis|nats] [--cluster] [--port 5000]

using OrderSystem.Configuration;
using OrderSystem.Extensions;

var builder = WebApplication.CreateSlimBuilder(args);

// Parse command line arguments or configuration
var transport = GetArg(args, "--transport") 
    ?? builder.Configuration["Catga:Transport"] 
    ?? "inmemory";
var persistence = GetArg(args, "--persistence") 
    ?? builder.Configuration["Catga:Persistence"] 
    ?? "inmemory";
var redisConn = GetArg(args, "--redis") 
    ?? builder.Configuration["Catga:Redis:ConnectionString"] 
    ?? "localhost:6379";
var natsUrl = GetArg(args, "--nats") 
    ?? builder.Configuration["Catga:Nats:Url"] 
    ?? "nats://localhost:4222";
var isCluster = args.Contains("--cluster");
var nodeId = GetArg(args, "--node-id") ?? $"node-{Guid.NewGuid().ToString("N")[..6]}";
var port = int.Parse(GetArg(args, "--port") ?? "5000");

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Configure services
builder.Services.ConfigureCatgaServices(transport, persistence, redisConn, natsUrl, builder.Environment);
builder.Services.ConfigureJsonOptions();
builder.Services.AddSingleton(new NodeInfo(nodeId, isCluster, transport, persistence));

var app = builder.Build();

// Enable static files
app.UseStaticFiles();

// Map endpoints
app.MapOrderSystemEndpoints();

// Print startup banner
EndpointExtensions.PrintStartupBanner(nodeId, isCluster, port, transport, persistence);

app.Run();

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx < args.Length - 1 ? args[idx + 1] : null;
}

// Make Program accessible to tests
public partial class Program { }
