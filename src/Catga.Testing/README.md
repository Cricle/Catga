# Catga.Testing

测试辅助库 - 简化 Catga CQRS 应用的单元测试和集成测试。

## 📦 安装

```bash
dotnet add package Catga.Testing
```

## 🚀 快速开始

### 1. 使用 CatgaTestFixture

```csharp
using Catga.Testing;
using Xunit;

public class MyCommandTests : IDisposable
{
    private readonly CatgaTestFixture _fixture;

    public MyCommandTests()
    {
        _fixture = new CatgaTestFixture();
        
        // 注册你的 Handler
        _fixture.RegisterRequestHandler<MyCommand, MyResponse, MyCommandHandler>();
    }

    [Fact]
    public async Task SendCommand_ShouldReturnSuccess()
    {
        // Arrange
        var command = new MyCommand { Name = "Test" };

        // Act
        var result = await _fixture.Mediator.SendAsync(command);

        // Assert
        result.Should().BeSuccessful();
        result.Value.Name.Should().Be("Test");
    }

    public void Dispose() => _fixture.Dispose();
}
```

### 2. 使用 Mock Handlers

```csharp
using Catga.Testing;

public class MyTests
{
    [Fact]
    public async Task UseTrackableHandler()
    {
        var fixture = new CatgaTestFixture();
        var handler = new TrackableHandler<MyCommand, MyResponse>();
        
        fixture.ConfigureServices(services =>
        {
            services.AddSingleton<IRequestHandler<MyCommand, MyResponse>>(handler);
        });

        // 执行测试
        await fixture.Mediator.SendAsync(new MyCommand());

        // 验证调用
        handler.CallCount.Should().Be(1);
        handler.LastRequest.Should().NotBeNull();
    }
}
```

### 3. 使用 FluentAssertions 扩展

```csharp
using Catga.Testing.Assertions;

[Fact]
public async Task TestWithAssertions()
{
    var fixture = new CatgaTestFixture();
    var result = await fixture.Mediator.SendAsync(new MyCommand());

    // 使用 Catga 专用断言
    result.Should().BeSuccessful();
    result.Should().HaveValue(expectedValue);
    result.Should().HaveValueSatisfying(value =>
    {
        value.Name.Should().Be("Test");
        value.Count.Should().BeGreaterThan(0);
    });
}

[Fact]
public async Task TestFailure()
{
    var fixture = new CatgaTestFixture();
    var result = await fixture.Mediator.SendAsync(new FailingCommand());

    result.Should().BeFailure();
    result.Should().BeFailureWithError("Expected error message");
}
```

### 4. 使用测试消息构建器

```csharp
using Catga.Testing.Builders;

[Fact]
public void BuildTestMessage()
{
    var command = TestMessageBuilderExtensions
        .CreateCommand<MyCommand>()
        .WithMessageId(12345)
        .WithCorrelationId(67890)
        .WithQoS(QualityOfService.AtLeastOnce)
        .Configure(cmd => cmd.Name = "Test")
        .Build();

    command.MessageId.Should().Be(12345);
    command.CorrelationId.Should().Be(67890);
}
```

## 📚 功能特性

### CatgaTestFixture

测试固件基类，提供：
- ✅ 自动配置 Catga 服务
- ✅ 内存传输（无需外部依赖）
- ✅ Fluent API 注册 Handlers
- ✅ 获取 Mediator 和其他服务

### Mock Handlers

预制的 Mock Handler：

| Handler | 用途 |
|---------|------|
| `MockSuccessHandler<TRequest, TResponse>` | 总是返回成功 |
| `MockFailureHandler<TRequest, TResponse>` | 总是返回失败 |
| `TrackableHandler<TRequest, TResponse>` | 记录调用次数和请求 |
| `TrackableEventHandler<TEvent>` | 记录事件调用 |
| `DelayedHandler<TRequest, TResponse>` | 模拟慢速操作 |
| `ExceptionHandler<TRequest, TResponse>` | 总是抛出异常 |

### FluentAssertions 扩展

专为 `CatgaResult<T>` 设计的断言：

```csharp
result.Should().BeSuccessful();
result.Should().BeFailure();
result.Should().BeFailureWithError("error message");
result.Should().HaveValue(expectedValue);
result.Should().HaveValueSatisfying(value => { /* assertions */ });
```

### 测试消息

预制的测试消息类型：
- `TestCommand` - 测试用命令
- `TestQuery` - 测试用查询
- `TestResponse` - 测试用响应
- `TestEvent` - 测试用事件
- `SimpleTestCommand` - 简单命令
- `SimpleTestEvent` - 简单事件

## 🎯 高级用法

### 自定义服务配置

```csharp
var fixture = new CatgaTestFixture()
    .ConfigureServices(services =>
    {
        // 添加自定义服务
        services.AddSingleton<IMyService, MyServiceMock>();
        
        // 覆盖默认服务
        services.AddScoped<IMessageTransport, MyCustomTransport>();
    });
```

### 测试事件处理

```csharp
var eventHandler = new TrackableEventHandler<MyEvent>();

var fixture = new CatgaTestFixture()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IEventHandler<MyEvent>>(eventHandler);
    });

await fixture.Mediator.PublishAsync(new MyEvent { Data = "test" });

eventHandler.CallCount.Should().Be(1);
eventHandler.LastEvent.Data.Should().Be("test");
```

### 测试异常处理

```csharp
var fixture = new CatgaTestFixture();
fixture.ConfigureServices(services =>
{
    services.AddScoped<IRequestHandler<MyCommand, MyResponse>>(
        _ => new ExceptionHandler<MyCommand, MyResponse>(
            new InvalidOperationException("Test error")
        )
    );
});

Func<Task> act = async () => await fixture.Mediator.SendAsync(new MyCommand());

await act.Should().ThrowAsync<InvalidOperationException>()
    .WithMessage("Test error");
```

### 测试延迟操作

```csharp
var fixture = new CatgaTestFixture();
fixture.ConfigureServices(services =>
{
    services.AddScoped<IRequestHandler<MyCommand, MyResponse>>(
        _ => new DelayedHandler<MyCommand, MyResponse>(TimeSpan.FromSeconds(1))
    );
});

var sw = Stopwatch.StartNew();
await fixture.Mediator.SendAsync(new MyCommand());
sw.Stop();

sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
```

## 💡 最佳实践

1. **使用 CatgaTestFixture** - 简化测试设置
2. **使用 TrackableHandler** - 验证 Handler 调用
3. **使用 FluentAssertions** - 清晰的断言语法
4. **隔离测试** - 每个测试使用独立的 Fixture
5. **Dispose Fixture** - 确保资源清理

## 📖 示例

完整的测试示例请参考：
- [tests/Catga.Tests/](../../tests/Catga.Tests/) - 项目测试
- [examples/OrderSystem.Api/](../../examples/OrderSystem.Api/) - 实际应用测试

## 🤝 贡献

欢迎提交 Issue 和 PR！

## 📄 许可证

MIT License - 详见 [LICENSE](../../LICENSE)

