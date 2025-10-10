using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga - 只需 2 行
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// API with error handling
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, UserResponse>(cmd);
    
    if (!result.IsSuccess)
    {
        // 使用详细错误信息
        if (result.DetailedError != null)
        {
            return result.DetailedError.Category switch
            {
                ErrorCategory.Validation => Results.BadRequest(new { 
                    error = result.DetailedError.Code,
                    message = result.DetailedError.Message,
                    details = result.DetailedError.Details
                }),
                ErrorCategory.Business => Results.Conflict(new {
                    error = result.DetailedError.Code,
                    message = result.DetailedError.Message
                }),
                _ => Results.Problem(result.DetailedError.Message)
            };
        }
        
        return Results.BadRequest(result.Error);
    }
    
    return Results.Ok(result.Value);
});

app.MapGet("/users/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync<GetUserQuery, UserResponse>(new(id));
    
    if (!result.IsSuccess)
    {
        if (result.DetailedError?.Category == ErrorCategory.NotFound)
        {
            return Results.NotFound(new { 
                error = result.DetailedError.Code,
                message = result.DetailedError.Message
            });
        }
        
        return Results.Problem(result.Error);
    }
    
    return Results.Ok(result.Value);
});

app.Run();

// ==================== 消息 ====================

public record CreateUserCommand(
    [Required][StringLength(50)] string Username, 
    [Required][EmailAddress] string Email
) : MessageBase, IRequest<UserResponse>;

public record GetUserQuery(string UserId) : MessageBase, IRequest<UserResponse>;
public record UserResponse(string UserId, string Username, string Email);

// ==================== Handler（自动注册）====================

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    private readonly ILogger<CreateUserHandler> _logger;
    
    // 模拟数据库
    private static readonly HashSet<string> _usernames = new();

    public CreateUserHandler(ILogger<CreateUserHandler> logger) => _logger = logger;

    public Task<CatgaResult<UserResponse>> HandleAsync(CreateUserCommand cmd, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating user: {Username}", cmd.Username);

        // 验证：用户名已存在
        if (_usernames.Contains(cmd.Username))
        {
            return Task.FromResult(CatgaResult<UserResponse>.Failure(
                CatgaError.Business(
                    "USER_001",
                    $"用户名 '{cmd.Username}' 已存在",
                    $"Duplicate username: {cmd.Username}"
                )
            ));
        }

        // 验证：邮箱格式
        if (!cmd.Email.Contains('@'))
        {
            return Task.FromResult(CatgaResult<UserResponse>.Failure(
                CatgaError.Validation(
                    "USER_002",
                    "邮箱格式无效",
                    $"Invalid email format: {cmd.Email}"
                )
            ));
        }

        // 创建用户
        var userId = Guid.NewGuid().ToString();
        _usernames.Add(cmd.Username);

        _logger.LogInformation("User created successfully: {UserId}", userId);

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
            return Task.FromResult(CatgaResult<UserResponse>.Failure(
                CatgaError.NotFound(
                    "USER_003",
                    $"用户 '{query.UserId}' 不存在"
                )
            ));
        }

        // 返回模拟数据
        return Task.FromResult(CatgaResult<UserResponse>.Success(
            new UserResponse(query.UserId, "John Doe", "john@example.com")
        ));
    }
}
