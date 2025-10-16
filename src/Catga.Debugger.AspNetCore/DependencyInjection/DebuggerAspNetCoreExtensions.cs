using Catga.Debugger.AspNetCore.Endpoints;
using Catga.Debugger.AspNetCore.Hubs;
using Catga.Debugger.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catga.Debugger.AspNetCore.DependencyInjection;

/// <summary>ASP.NET Core extensions for Catga.Debugger</summary>
public static class DebuggerAspNetCoreExtensions
{
    /// <summary>Add Catga debugger with ASP.NET Core integration</summary>
    public static IServiceCollection AddCatgaDebuggerWithAspNetCore(
        this IServiceCollection services,
        Action<Catga.Debugger.Models.ReplayOptions>? configureOptions = null)
    {
        // Add core debugger services
        services.AddCatgaDebugger(configureOptions);

        // Add SignalR
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false; // Disable for production
            options.MaximumReceiveMessageSize = 102400; // 100KB limit
            options.StreamBufferCapacity = 10;
        })
        .AddJsonProtocol(options =>
        {
            // Configure AOT-friendly JSON options
            options.PayloadSerializerOptions.PropertyNameCaseInsensitive = false;
            options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.PayloadSerializerOptions.WriteIndented = false;

            // Add source-generated context if available
            // options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, DebuggerJsonContext.Default);
        });

        // Add notification service
        services.AddHostedService<DebuggerNotificationService>();

        // Configure JSON options for minimal APIs
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.WriteIndented = false;
        });

        return services;
    }

    /// <summary>Map Catga debugger endpoints and hub</summary>
    public static IEndpointRouteBuilder MapCatgaDebugger(
        this IEndpointRouteBuilder endpoints,
        string basePath = "/debug")
    {
        var app = endpoints as IApplicationBuilder
            ?? throw new InvalidOperationException("endpoints must be IApplicationBuilder");

        // Map API endpoints
        endpoints.MapCatgaDebuggerApi();

        // Map SignalR hub
        endpoints.MapHub<DebuggerHub>($"{basePath}/hub")
            .WithMetadata(new Microsoft.AspNetCore.Cors.EnableCorsAttribute());

        // Serve static files for Vue 3 UI
        var staticFilesPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "debugger");
        if (Directory.Exists(staticFilesPath))
        {
            app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(staticFilesPath),
                RequestPath = basePath
            });
        }

        // SPA fallback for Vue Router
        endpoints.MapFallback($"{basePath}/{{**path}}", async context =>
        {
            var indexPath = Path.Combine(staticFilesPath, "index.html");
            if (File.Exists(indexPath))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Debugger UI not found. Please build the Vue 3 UI first.");
            }
        });

        return endpoints;
    }
}

// TODO: Add source-generated JSON context for AOT
// [JsonSerializable(typeof(FlowsResponse))]
// [JsonSerializable(typeof(FlowResponse))]
// [JsonSerializable(typeof(StatsResponse))]
// [JsonSerializable(typeof(SystemReplayRequest))]
// [JsonSerializable(typeof(SystemReplayResponse))]
// [JsonSerializable(typeof(FlowReplayRequest))]
// [JsonSerializable(typeof(FlowReplayResponse))]
// [JsonSerializable(typeof(FlowEventUpdate))]
// [JsonSerializable(typeof(StatsUpdate))]
// [JsonSerializable(typeof(ReplayProgressUpdate))]
// public partial class DebuggerJsonContext : JsonSerializerContext
// {
// }

