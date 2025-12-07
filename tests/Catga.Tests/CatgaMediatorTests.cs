using Catga;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests;

/// <summary>
/// CatgaMediator 核心功能测试
/// 每个 Command/Query 只有一个 Handler，符合 CQRS 原则
/// </summary>
public class CatgaMediatorTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICatgaMediator _mediator;

    public CatgaMediatorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        // 注册测试 handlers
        services.AddScoped<IRequestHandler<CreateUserCommand, CreateUserResponse>, CreateUserCommandHandler>();
        services.AddScoped<IRequestHandler<GetUserQuery, GetUserResponse>, GetUserQueryHandler>();
        services.AddScoped<IEventHandler<UserCreatedEvent>, UserCreatedEventHandler>();
        services.AddScoped<IEventHandler<UserCreatedEvent>, SendWelcomeEmailHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [Fact]
    public async Task SendAsync_Command_ShouldReturnSuccess()
    {
        // Arrange
        var command = new CreateUserCommand("Alice", "alice@example.com");

        // Act
        var result = await _mediator.SendAsync<CreateUserCommand, CreateUserResponse>(command);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Alice", result.Value.Name);
        Assert.NotEqual(0, result.Value.UserId);
    }

    [Fact]
    public async Task SendAsync_Query_ShouldReturnSuccess()
    {
        // Arrange
        var query = new GetUserQuery(123);

        // Act
        var result = await _mediator.SendAsync<GetUserQuery, GetUserResponse>(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(123, result.Value.UserId);
    }

    [Fact]
    public async Task PublishAsync_Event_ShouldExecuteAllHandlers()
    {
        // Arrange
        var @event = new UserCreatedEvent(100, "Bob", "bob@example.com");

        // Act & Assert (不抛异常即成功)
        await _mediator.PublishAsync(@event);
    }

    [Fact]
    public async Task SendBatchAsync_ShouldProcessMultipleCommands()
    {
        // Arrange
        var commands = new List<CreateUserCommand>
        {
            new("User1", "user1@example.com"),
            new("User2", "user2@example.com"),
            new("User3", "user3@example.com")
        };

        // Act
        var results = await _mediator.SendBatchAsync<CreateUserCommand, CreateUserResponse>(commands);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task PublishBatchAsync_ShouldProcessMultipleEvents()
    {
        // Arrange
        var events = new List<UserCreatedEvent>
        {
            new(1, "User1", "user1@example.com"),
            new(2, "User2", "user2@example.com"),
            new(3, "User3", "user3@example.com")
        };

        // Act & Assert (不抛异常即成功)
        await _mediator.PublishBatchAsync(events);
    }
}

// ==================== 测试消息定义 ====================

public partial record CreateUserCommand(string Name, string Email) : IRequest<CreateUserResponse>
{
    public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
}
public partial record CreateUserResponse(long UserId, string Name, string Email);

public partial record GetUserQuery(long UserId) : IRequest<GetUserResponse>
{
    public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
}
public partial record GetUserResponse(long UserId, string Name, string Email);

public partial record UserCreatedEvent(long UserId, string Name, string Email) : IEvent
{
    public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
}

// ==================== 测试 Handlers ====================

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    private static long _nextUserId = 1000;

    public ValueTask<CatgaResult<CreateUserResponse>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        var userId = Interlocked.Increment(ref _nextUserId);
        var response = new CreateUserResponse(userId, request.Name, request.Email);
        return new ValueTask<CatgaResult<CreateUserResponse>>(CatgaResult<CreateUserResponse>.Success(response));
    }
}

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, GetUserResponse>
{
    public ValueTask<CatgaResult<GetUserResponse>> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        // 模拟从数据库查询
        var response = new GetUserResponse(request.UserId, "MockUser", "mock@example.com");
        return new ValueTask<CatgaResult<GetUserResponse>>(CatgaResult<GetUserResponse>.Success(response));
    }
}

public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    public ValueTask HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // 模拟事件处理（如保存到事件存储）
        return ValueTask.CompletedTask;
    }
}

public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
{
    public ValueTask HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // 模拟发送欢迎邮件
        return ValueTask.CompletedTask;
    }
}

