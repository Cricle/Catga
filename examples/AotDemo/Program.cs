using Catga;
using Catga.DependencyInjection;
using Catga.Messages;
using Catga.Handlers;
using Catga.Nats.Serialization;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

// ============================================
// AOT 兼容性演示程序
// 此程序展示 Catga 在 NativeAOT 编译下的功能
// ============================================

var builder = Host.CreateApplicationBuilder(args);

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 注册 Catga（100% AOT 兼容）
builder.Services.AddCatga(options =>
{
    options.EnableDetailedErrors = true;
});

// 注册 JSON 序列化上下文（完全消除 AOT 警告）
NatsJsonSerializer.SetCustomOptions(new System.Text.Json.JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
        AppJsonContext.Default,           // 应用程序类型
        NatsCatgaJsonContext.Default      // Catga 框架类型
    )
});

// 注册处理器
builder.Services.AddRequestHandler<CalculateCommand, int, CalculateHandler>();
builder.Services.AddRequestHandler<GetStatusQuery, string, GetStatusQueryHandler>();

var app = builder.Build();

// 测试 CQRS
var mediator = app.Services.GetRequiredService<ICatgaMediator>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("=== Catga AOT Compatibility Demo ===");
logger.LogInformation("");

// 测试 Command
logger.LogInformation("Testing Command...");
var calculateCmd = new CalculateCommand { A = 10, B = 5 };
var calculateResult = await mediator.SendAsync(calculateCmd);
logger.LogInformation("Calculate result: {Result} (Success: {Success})",
    calculateResult.Value, calculateResult.IsSuccess);

// 测试 Query
logger.LogInformation("");
logger.LogInformation("Testing Query...");
var statusQuery = new GetStatusQuery();
var statusResult = await mediator.SendAsync(statusQuery);
logger.LogInformation("Status result: {Result} (Success: {Success})",
    statusResult.Value, statusResult.IsSuccess);

logger.LogInformation("");
logger.LogInformation("=== All tests passed! AOT compilation successful! ===");

return 0;

// ============================================
// 消息定义
// ============================================

public record CalculateCommand : IRequest<int>
{
    public required int A { get; init; }
    public required int B { get; init; }
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

public record GetStatusQuery : IRequest<string>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

// ============================================
// 处理器定义
// ============================================

public class CalculateHandler : IRequestHandler<CalculateCommand, int>
{
    private readonly ILogger<CalculateHandler> _logger;

    public CalculateHandler(ILogger<CalculateHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<int>> HandleAsync(CalculateCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Calculating {A} + {B}", request.A, request.B);
        var result = request.A + request.B;
        return await Task.FromResult(CatgaResult<int>.Success(result));
    }
}

public class GetStatusQueryHandler : IRequestHandler<GetStatusQuery, string>
{
    private readonly ILogger<GetStatusQueryHandler> _logger;

    public GetStatusQueryHandler(ILogger<GetStatusQueryHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult<string>> HandleAsync(GetStatusQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting status");
        return Task.FromResult(CatgaResult<string>.Success("System is running with NativeAOT!"));
    }
}

// ============================================
// JSON 源生成上下文 (AOT 必需)
// ============================================

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
// 应用程序消息类型
[JsonSerializable(typeof(CalculateCommand))]
[JsonSerializable(typeof(GetStatusQuery))]
// 结果类型
[JsonSerializable(typeof(CatgaResult<int>))]
[JsonSerializable(typeof(CatgaResult<string>))]
[JsonSerializable(typeof(CatgaResult))]
// 基础类型
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
public partial class AppJsonContext : JsonSerializerContext
{
}

