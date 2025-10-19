using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Catga.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Catga.AspNetCore;

/// <summary>
/// Catga 诊断端点，提供健康检查、指标和调试信息
/// </summary>
public static class CatgaDiagnosticsEndpoint
{
    /// <summary>
    /// 映射 Catga 诊断端点
    /// </summary>
    /// <remarks>
    /// 诊断端点主要用于开发和调试环境，不建议在生产环境启用。
    /// 这些端点使用反射，不支持 Native AOT。
    /// </remarks>
#pragma warning disable IL2026, IL3050 // Diagnostic endpoints are not AOT compatible, intended for development only
    public static IEndpointRouteBuilder MapCatgaDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pathPrefix = "/catga")
    {
        var group = endpoints.MapGroup(pathPrefix)
            .WithTags("Catga Diagnostics")
            .WithDisplayName("Catga Diagnostics");

        // 主仪表板页面
        group.MapGet("/", GetDashboard)
            .WithName("CatgaDashboard")
            .WithSummary("Catga diagnostic dashboard")
            .Produces<string>(200, "text/html");

        // 健康检查端点
        group.MapGet("/health", GetHealth)
            .WithName("CatgaHealth")
            .WithSummary("Get Catga health status")
            .Produces<CatgaHealthResponse>(200);

        // 指标端点
        group.MapGet("/metrics", GetMetrics)
            .WithName("CatgaMetrics")
            .WithSummary("Get Catga metrics")
            .Produces<CatgaMetricsResponse>(200);

        // 活动追踪端点
        group.MapGet("/activities", GetActivities)
            .WithName("CatgaActivities")
            .WithSummary("Get recent Catga activities")
            .Produces<CatgaActivitiesResponse>(200);

        return endpoints;
    }
#pragma warning restore IL2026, IL3050

    private static IResult GetDashboard()
    {
        var html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Catga Diagnostics Dashboard</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
            padding: 20px;
            min-height: 100vh;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
        }
        .header {
            text-align: center;
            color: white;
            margin-bottom: 30px;
        }
        .header h1 {
            font-size: 2.5rem;
            margin-bottom: 10px;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.2);
        }
        .header p {
            font-size: 1.1rem;
            opacity: 0.9;
        }
        .grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 20px;
            margin-bottom: 20px;
        }
        .card {
            background: white;
            border-radius: 12px;
            padding: 20px;
            box-shadow: 0 4px 6px rgba(0,0,0,0.1);
            transition: transform 0.2s;
        }
        .card:hover {
            transform: translateY(-5px);
            box-shadow: 0 8px 12px rgba(0,0,0,0.15);
        }
        .card h2 {
            font-size: 1.3rem;
            margin-bottom: 15px;
            color: #667eea;
            display: flex;
            align-items: center;
            gap: 10px;
        }
        .status {
            display: inline-block;
            padding: 4px 12px;
            border-radius: 20px;
            font-size: 0.85rem;
            font-weight: 600;
        }
        .status.healthy { background: #10b981; color: white; }
        .status.warning { background: #f59e0b; color: white; }
        .status.error { background: #ef4444; color: white; }
        .metric {
            display: flex;
            justify-content: space-between;
            padding: 10px 0;
            border-bottom: 1px solid #f0f0f0;
        }
        .metric:last-child { border-bottom: none; }
        .metric-label {
            color: #666;
            font-size: 0.95rem;
        }
        .metric-value {
            font-weight: 600;
            color: #333;
        }
        .endpoint-list {
            list-style: none;
        }
        .endpoint-list li {
            padding: 8px 0;
            border-bottom: 1px solid #f0f0f0;
        }
        .endpoint-list li:last-child { border-bottom: none; }
        .endpoint-list a {
            color: #667eea;
            text-decoration: none;
            font-weight: 500;
        }
        .endpoint-list a:hover {
            text-decoration: underline;
        }
        .footer {
            text-align: center;
            color: white;
            margin-top: 30px;
            opacity: 0.8;
            font-size: 0.9rem;
        }
        .loading {
            text-align: center;
            padding: 20px;
            color: #666;
        }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🐱 Catga Diagnostics Dashboard</h1>
            <p>Real-time monitoring and diagnostics for your Catga application</p>
        </div>

        <div class=""grid"">
            <div class=""card"">
                <h2>💚 Health Status</h2>
                <div id=""health-content"" class=""loading"">Loading...</div>
            </div>

            <div class=""card"">
                <h2>📊 Metrics</h2>
                <div id=""metrics-content"" class=""loading"">Loading...</div>
            </div>

            <div class=""card"">
                <h2>🔍 Recent Activities</h2>
                <div id=""activities-content"" class=""loading"">Loading...</div>
            </div>
        </div>

        <div class=""card"">
            <h2>🔗 API Endpoints</h2>
            <ul class=""endpoint-list"">
                <li><a href=""./health"" target=""_blank"">GET /health</a> - Health check status</li>
                <li><a href=""./metrics"" target=""_blank"">GET /metrics</a> - Application metrics</li>
                <li><a href=""./activities"" target=""_blank"">GET /activities</a> - Recent activities</li>
            </ul>
        </div>

        <div class=""footer"">
            <p>Powered by Catga Framework • Fast & Cute 🐱⚡</p>
        </div>
    </div>

    <script>
        async function fetchData(url, elementId, renderer) {
            try {
                const response = await fetch(url);
                const data = await response.json();
                document.getElementById(elementId).innerHTML = renderer(data);
            } catch (error) {
                document.getElementById(elementId).innerHTML = `<div style=""color: #ef4444;"">Error: ${error.message}</div>`;
            }
        }

        function renderHealth(data) {
            const statusClass = data.status === 'Healthy' ? 'healthy' : 
                               data.status === 'Degraded' ? 'warning' : 'error';
            return `
                <div class=""metric"">
                    <span class=""metric-label"">Status</span>
                    <span class=""status ${statusClass}"">${data.status}</span>
                </div>
                <div class=""metric"">
                    <span class=""metric-label"">Uptime</span>
                    <span class=""metric-value"">${data.uptime}</span>
                </div>
                <div class=""metric"">
                    <span class=""metric-label"">Framework Version</span>
                    <span class=""metric-value"">${data.frameworkVersion}</span>
                </div>
            `;
        }

        function renderMetrics(data) {
            return `
                <div class=""metric"">
                    <span class=""metric-label"">Total Messages</span>
                    <span class=""metric-value"">${data.totalMessages.toLocaleString()}</span>
                </div>
                <div class=""metric"">
                    <span class=""metric-label"">Commands Processed</span>
                    <span class=""metric-value"">${data.commandsProcessed.toLocaleString()}</span>
                </div>
                <div class=""metric"">
                    <span class=""metric-label"">Events Published</span>
                    <span class=""metric-value"">${data.eventsPublished.toLocaleString()}</span>
                </div>
                <div class=""metric"">
                    <span class=""metric-label"">Errors</span>
                    <span class=""metric-value"" style=""color: ${data.errors > 0 ? '#ef4444' : '#10b981'}"">${data.errors}</span>
                </div>
            `;
        }

        function renderActivities(data) {
            if (data.recentActivities.length === 0) {
                return '<div style=""color: #666; padding: 10px 0;"">No recent activities</div>';
            }
            return data.recentActivities.slice(0, 5).map(activity => `
                <div class=""metric"">
                    <span class=""metric-label"">${activity.type}</span>
                    <span class=""metric-value"" style=""font-size: 0.85rem; color: #666;"">${activity.timestamp}</span>
                </div>
            `).join('');
        }

        // Load data
        fetchData('./health', 'health-content', renderHealth);
        fetchData('./metrics', 'metrics-content', renderMetrics);
        fetchData('./activities', 'activities-content', renderActivities);

        // Auto refresh every 5 seconds
        setInterval(() => {
            fetchData('./health', 'health-content', renderHealth);
            fetchData('./metrics', 'metrics-content', renderMetrics);
            fetchData('./activities', 'activities-content', renderActivities);
        }, 5000);
    </script>
</body>
</html>";

        return Results.Content(html, "text/html", Encoding.UTF8);
    }

    private static IResult GetHealth()
    {
        var startTime = Process.GetCurrentProcess().StartTime;
        var uptime = DateTime.Now - startTime;

        var response = new CatgaHealthResponse
        {
            Status = "Healthy",
            Uptime = FormatUptime(uptime),
            FrameworkVersion = typeof(ICatgaMediator).Assembly.GetName().Version?.ToString() ?? "Unknown",
            Timestamp = DateTime.UtcNow
        };

        return Results.Ok(response);
    }

    private static IResult GetMetrics()
    {
        // 这里可以从 CatgaMetrics 中获取实际的指标数据
        // 由于 Meter 的读取需要 MeterListener，这里提供模拟数据
        var response = new CatgaMetricsResponse
        {
            TotalMessages = 0, // 实际应从 Meter 读取
            CommandsProcessed = 0,
            EventsPublished = 0,
            Errors = 0,
            Timestamp = DateTime.UtcNow
        };

        return Results.Ok(response);
    }

    private static IResult GetActivities()
    {
        // 这里可以从 ActivitySource 中获取最近的活动
        // 由于需要 ActivityListener，这里提供示例数据
        var response = new CatgaActivitiesResponse
        {
            RecentActivities = new List<ActivityInfo>(),
            Timestamp = DateTime.UtcNow
        };

        return Results.Ok(response);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{(int)uptime.TotalSeconds}s";
    }
}

public record CatgaHealthResponse
{
    public string Status { get; init; } = "Unknown";
    public string Uptime { get; init; } = "0s";
    public string FrameworkVersion { get; init; } = "Unknown";
    public DateTime Timestamp { get; init; }
}

public record CatgaMetricsResponse
{
    public long TotalMessages { get; init; }
    public long CommandsProcessed { get; init; }
    public long EventsPublished { get; init; }
    public long Errors { get; init; }
    public DateTime Timestamp { get; init; }
}

public record CatgaActivitiesResponse
{
    public List<ActivityInfo> RecentActivities { get; init; } = new();
    public DateTime Timestamp { get; init; }
}

public record ActivityInfo
{
    public string Type { get; init; } = "";
    public string Timestamp { get; init; } = "";
    public string? Details { get; init; }
}

