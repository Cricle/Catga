# SimpleWebApi - Catga 极简示例

## 📖 简介

最简单的 Catga CQRS 示例，展示核心功能：
- ✨ **源生成器自动注册** - 零手动配置
- 📝 **Record 消息定义** - 1 行代码
- 🎯 **Command/Query 分离** - 清晰的 CQRS 模式

## 🚀 快速开始

### 运行示例

```bash
cd examples/SimpleWebApi
dotnet run
```

访问 Swagger: `https://localhost:5001/swagger`

### 测试 API

**创建用户**:
```bash
curl -X POST https://localhost:5001/users \
  -H "Content-Type: application/json" \
  -d '{"username": "john_doe", "email": "john@example.com"}'
```

**查询用户**:
```bash
curl https://localhost:5001/users/123
```

## 🎯 核心特性

### 1. 源生成器自动注册

```csharp
// ✨ 只需 2 行！
builder.Services.AddCatga();              // 注册 Catga 核心服务
builder.Services.AddGeneratedHandlers();  // 源生成器自动注册所有 Handler
```

**源生成器会自动发现所有实现了 `IRequestHandler` 或 `IEventHandler` 的类！**

### 2. 极简消息定义

```csharp
// 命令（1行）
public record CreateUserCommand(string Username, string Email) : MessageBase, IRequest<UserResponse>;

// 查询（1行）
public record GetUserQuery(string UserId) : MessageBase, IRequest<UserResponse>;

// 响应
public record UserResponse(string UserId, string Username, string Email);
```

### 3. Handler 自动注册

```csharp
// 🎯 无需任何特性标记，自动发现并注册！
public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    public Task<CatgaResult<UserResponse>> HandleAsync(CreateUserCommand cmd, CancellationToken ct)
    {
        // 业务逻辑
    }
}
```

**特点**:
- ✅ 零配置 - 实现接口即可
- ✅ 默认 Scoped 生命周期
- ✅ 编译时生成，零运行时开销
- ✅ 100% AOT 兼容

### 4. 可选：控制注册行为

```csharp
// 自定义生命周期
[CatgaHandler(Lifetime = HandlerLifetime.Singleton)]
public class MyHandler : IRequestHandler<MyCommand, MyResponse> { }

// 禁用自动注册（手动注册）
[CatgaHandler(AutoRegister = false)]
public class ManualHandler : IRequestHandler<ManualCommand, ManualResponse> { }
```

## 📊 代码统计

- **总行数**: 91 行
- **Handler 数量**: 2 个
- **消息定义**: 3 行
- **配置代码**: 2 行

## 🎓 学习要点

1. **消息定义**: 使用 Record 类型，继承 `MessageBase`
2. **Handler 实现**: 实现 `IRequestHandler<TRequest, TResponse>`
3. **自动注册**: 调用 `AddGeneratedHandlers()`，无需手动注册
4. **发送请求**: `await mediator.SendAsync<TRequest, TResponse>(request)`

## 📚 相关文档

- [Catga 快速开始](../../QUICK_START.md)
- [架构说明](../../ARCHITECTURE.md)
- [源生成器文档](../../src/Catga.SourceGenerator/README.md)
