using Catga;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

// === 定义消息 ===

// Command - 修改状态
public record CreateUserCommand(string Name, string Email) : IRequest<Guid>;

// Query - 只读查询
public record GetUserQuery(Guid UserId) : IRequest<UserDto?>;

// Event - 领域事件
public record UserCreatedEvent(Guid UserId, string Name) : INotification;

// DTO
public record UserDto(Guid Id, string Name, string Email);

// === 实现 Handlers ===

public class CreateUserHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly UserStore _store;
    private readonly IMediator _mediator;

    public CreateUserHandler(UserStore store, IMediator mediator)
    {
        _store = store;
        _mediator = mediator;
    }

    public async Task<CatgaResult<Guid>> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        _store.Users[userId] = new UserDto(userId, request.Name, request.Email);

        // 发布事件
        await _mediator.PublishAsync(new UserCreatedEvent(userId, request.Name), ct);

        return CatgaResult<Guid>.Success(userId);
    }
}

public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto?>
{
    private readonly UserStore _store;

    public GetUserHandler(UserStore store) => _store = store;

    public Task<CatgaResult<UserDto?>> Handle(GetUserQuery request, CancellationToken ct)
    {
        _store.Users.TryGetValue(request.UserId, out var user);
        return Task.FromResult(CatgaResult<UserDto?>.Success(user));
    }
}

public class UserCreatedEventHandler : INotificationHandler<UserCreatedEvent>
{
    public Task Handle(UserCreatedEvent notification, CancellationToken ct)
    {
        Console.WriteLine($"✅ User created: {notification.Name} (ID: {notification.UserId})");
        return Task.CompletedTask;
    }
}

// === 简单存储 ===
public class UserStore
{
    public Dictionary<Guid, UserDto> Users { get; } = new();
}

// === 配置和运行 ===
var services = new ServiceCollection();
services.AddSingleton<UserStore>();
services.AddCatga();
services.AddHandler<CreateUserCommand, Guid, CreateUserHandler>();
services.AddHandler<GetUserQuery, UserDto?, GetUserHandler>();
services.AddHandler<UserCreatedEvent, UserCreatedEventHandler>();

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// 创建用户
Console.WriteLine("Creating user...");
var createResult = await mediator.SendAsync(new CreateUserCommand("Alice", "alice@example.com"));
var userId = createResult.Data;

// 查询用户
Console.WriteLine("\nQuerying user...");
var queryResult = await mediator.SendAsync(new GetUserQuery(userId));
Console.WriteLine($"User: {queryResult.Data?.Name} ({queryResult.Data?.Email})");

