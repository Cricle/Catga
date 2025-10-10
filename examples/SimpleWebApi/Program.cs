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
builder.Services.AddGeneratedHandlers();  // 自动注册所有 Handler

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// API
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand cmd) =>
    await mediator.SendAsync<CreateUserCommand, UserResponse>(cmd) is var result && result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error));

app.MapGet("/users/{id}", async (ICatgaMediator mediator, string id) =>
    await mediator.SendAsync<GetUserQuery, UserResponse>(new(id)) is var result && result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.NotFound());

app.Run();

// ==================== 消息 ====================

public record CreateUserCommand(string Username, string Email) : MessageBase, IRequest<UserResponse>;
public record GetUserQuery(string UserId) : MessageBase, IRequest<UserResponse>;
public record UserResponse(string UserId, string Username, string Email);

// ==================== Handler（自动注册）====================

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    public Task<CatgaResult<UserResponse>> HandleAsync(CreateUserCommand cmd, CancellationToken ct = default)
    {
        var userId = Guid.NewGuid().ToString();
        return Task.FromResult(CatgaResult<UserResponse>.Success(
            new UserResponse(userId, cmd.Username, cmd.Email)));
    }
}

public class GetUserHandler : IRequestHandler<GetUserQuery, UserResponse>
{
    public Task<CatgaResult<UserResponse>> HandleAsync(GetUserQuery query, CancellationToken ct = default)
    {
        return Task.FromResult(CatgaResult<UserResponse>.Success(
            new UserResponse(query.UserId, "John Doe", "john@example.com")));
    }
}
