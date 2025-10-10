using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga - 只需 2 行
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// 创建用户
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, UserResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// 查询用户
app.MapGet("/users/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync<GetUserQuery, UserResponse>(new(id));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
});

app.Run();

// ==================== 消息 ====================

public record CreateUserCommand(string Username, string Email) : MessageBase, IRequest<UserResponse>;
public record GetUserQuery(string UserId) : MessageBase, IRequest<UserResponse>;
public record UserResponse(string UserId, string Username, string Email);

// ==================== Handler（源生成器自动注册）====================

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    private readonly ILogger<CreateUserHandler> _logger;
    private static readonly HashSet<string> _usernames = new();

    public CreateUserHandler(ILogger<CreateUserHandler> logger) => _logger = logger;

    public Task<CatgaResult<UserResponse>> HandleAsync(CreateUserCommand cmd, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating user: {Username}", cmd.Username);

        // 验证：用户名已存在
        if (_usernames.Contains(cmd.Username))
        {
            return Task.FromResult(CatgaResult<UserResponse>.Failure($"用户名 '{cmd.Username}' 已存在"));
        }

        // 验证：邮箱格式
        if (!cmd.Email.Contains('@'))
        {
            return Task.FromResult(CatgaResult<UserResponse>.Failure("邮箱格式无效"));
        }

        // 创建用户
        var userId = Guid.NewGuid().ToString();
        _usernames.Add(cmd.Username);

        return Task.FromResult(CatgaResult<UserResponse>.Success(
            new UserResponse(userId, cmd.Username, cmd.Email)
        ));
    }
}

public class GetUserHandler : IRequestHandler<GetUserQuery, UserResponse>
{
    private readonly ILogger<GetUserHandler> _logger;

    public GetUserHandler(ILogger<GetUserHandler> logger) => _logger = logger;

    public Task<CatgaResult<UserResponse>> HandleAsync(GetUserQuery query, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting user: {UserId}", query.UserId);

        // 模拟：用户不存在
        if (query.UserId == "999")
        {
            return Task.FromResult(CatgaResult<UserResponse>.Failure($"用户 '{query.UserId}' 不存在"));
        }

        // 返回模拟数据
        return Task.FromResult(CatgaResult<UserResponse>.Success(
            new UserResponse(query.UserId, "John Doe", "john@example.com")
        ));
    }
}
