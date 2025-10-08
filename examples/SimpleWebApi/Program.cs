using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization.Json;
using SimpleWebApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🎯 Simplified Catga registration with Source Generator
builder.Services.AddCatga(options =>
{
    options.EnableLogging = true;
});

// Add JSON serializer
builder.Services.AddSingleton<Catga.Serialization.IMessageSerializer, JsonMessageSerializer>();

// 🆔 Add distributed ID generator (auto-detects worker ID)
builder.Services.AddDistributedId();

// ✨ Auto-register all handlers using source generator (No manual registration needed!)
builder.Services.AddGeneratedHandlers();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 📝 Simple API endpoints using Catga mediator
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand command) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, CreateUserResponse>(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("CreateUser");

app.MapGet("/users/{id}", async (ICatgaMediator mediator, string id) =>
{
    var query = new GetUserQuery { UserId = id };
    var result = await mediator.SendAsync<GetUserQuery, UserDto>(query);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
})
.WithName("GetUser");

// 🆔 Map distributed ID endpoints
app.MapDistributedIdEndpoints();

app.Run();

// ========================
// Domain Messages
// ========================

/// <summary>
/// Create user command
/// </summary>
public record CreateUserCommand : IRequest<CreateUserResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string Username { get; init; }
    public required string Email { get; init; }
}

public record CreateUserResponse
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
}

public record GetUserQuery : IRequest<UserDto>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string UserId { get; init; }
}

public record UserDto
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
}

public record UserCreatedEvent : IEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    public required string UserId { get; init; }
    public required string Username { get; init; }
}

// ========================
// Handlers (Auto-discovered by Source Generator!)
// ========================

/// <summary>
/// Create user command handler
/// No [CatgaHandler] attribute needed - automatically discovered!
/// </summary>
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    private readonly ILogger<CreateUserCommandHandler> _logger;
    private readonly ICatgaMediator _mediator;

    public CreateUserCommandHandler(
        ILogger<CreateUserCommandHandler> logger,
        ICatgaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<CatgaResult<CreateUserResponse>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating user: {Username}", request.Username);

        // Simulate database save
        var userId = Guid.NewGuid().ToString();

        // Publish domain event
        var @event = new UserCreatedEvent
        {
            UserId = userId,
            Username = request.Username,
            CorrelationId = request.CorrelationId
        };

        await _mediator.PublishAsync(@event, cancellationToken);

        var response = new CreateUserResponse
        {
            UserId = userId,
            Username = request.Username,
            Email = request.Email
        };

        return CatgaResult<CreateUserResponse>.Success(response);
    }
}

/// <summary>
/// Get user query handler
/// Also automatically discovered!
/// </summary>
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly ILogger<GetUserQueryHandler> _logger;

    public GetUserQueryHandler(ILogger<GetUserQueryHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult<UserDto>> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting user: {UserId}", request.UserId);

        // Simulate database query
        var user = new UserDto
        {
            UserId = request.UserId,
            Username = "john_doe",
            Email = "john@example.com"
        };

        return Task.FromResult(CatgaResult<UserDto>.Success(user));
    }
}

/// <summary>
/// User created event handler
/// Handle side effects when user is created
/// </summary>
public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedEventHandler> _logger;

    public UserCreatedEventHandler(ILogger<UserCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("User created event received: {UserId} - {Username}",
            @event.UserId, @event.Username);

        // Send welcome email, update analytics, etc.

        return Task.CompletedTask;
    }
}
