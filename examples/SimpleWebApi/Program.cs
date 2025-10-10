using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga - 只需 2 行！
builder.Services.AddCatga();              // 注册 Catga 核心服务
builder.Services.AddGeneratedHandlers();  // 源生成器自动注册所有 Handler ✨

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// API 端点
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, UserResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

app.MapGet("/users/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync<GetUserQuery, UserResponse>(new(id));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
});

app.Run();

// ==================== 消息定义 ====================

// 命令（1行）
public record CreateUserCommand(string Username, string Email) : MessageBase, IRequest<UserResponse>;

// 查询（1行）
public record GetUserQuery(string UserId) : MessageBase, IRequest<UserResponse>;

// 响应
public record UserResponse(string UserId, string Username, string Email);

// ==================== Handler ====================
// 🎯 所有 Handler 自动发现并注册 - 无需手动配置！

// 创建用户 Handler（自动注册为 Scoped）
public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    private readonly ILogger<CreateUserHandler> _logger;

    public CreateUserHandler(ILogger<CreateUserHandler> logger) => _logger = logger;

    public Task<CatgaResult<UserResponse>> HandleAsync(CreateUserCommand cmd, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating user: {Username}", cmd.Username);

        // TODO: 保存到数据库
        var userId = Guid.NewGuid().ToString();
        var response = new UserResponse(userId, cmd.Username, cmd.Email);

        return Task.FromResult(CatgaResult<UserResponse>.Success(response));
    }
}

// 查询用户 Handler（自动注册为 Scoped）
public class GetUserHandler : IRequestHandler<GetUserQuery, UserResponse>
{
    private readonly ILogger<GetUserHandler> _logger;

    public GetUserHandler(ILogger<GetUserHandler> logger) => _logger = logger;

    public Task<CatgaResult<UserResponse>> HandleAsync(GetUserQuery query, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting user: {UserId}", query.UserId);

        // TODO: 从数据库查询
        var response = new UserResponse(query.UserId, "John Doe", "john@example.com");

        return Task.FromResult(CatgaResult<UserResponse>.Success(response));
    }
}
