# Catga 分析器

Catga 提供 Roslyn 分析器，在编译时帮助发现常见错误和性能问题。

## 📊 分析器规则

### 性能规则 (CAT1xxx)

#### CAT1001: 缺少 AOT 属性
**严重性**: Warning

Handler 应标记 `[DynamicallyAccessedMembers]` 以支持 Native AOT。

```csharp
// ❌ 错误
public class MyHandler : IRequestHandler<MyCommand, bool>
{
    public Task<CatgaResult<bool>> Handle(...) { }
}

// ✅ 正确
public class MyHandler : IRequestHandler<MyCommand, bool>
{
    public Task<CatgaResult<bool>> Handle(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] MyCommand request,
        CancellationToken ct) { }
}
```

#### CAT1002: 异步方法中的阻塞调用
**严重性**: Warning

避免在异步 Handler 中使用 `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`。

```csharp
// ❌ 错误
public async Task<CatgaResult<bool>> Handle(MyCommand request, CancellationToken ct)
{
    var result = _service.DoSomethingAsync().Result; // Blocking!
    return CatgaResult<bool>.Success(result);
}

// ✅ 正确
public async Task<CatgaResult<bool>> Handle(MyCommand request, CancellationToken ct)
{
    var result = await _service.DoSomethingAsync(); // Non-blocking
    return CatgaResult<bool>.Success(result);
}
```

#### CAT1003: Handler 中使用反射
**严重性**: Warning

反射不支持 Native AOT，应使用源生成器。

```csharp
// ❌ 错误
public Task<CatgaResult<bool>> Handle(MyCommand request, CancellationToken ct)
{
    var type = Type.GetType(request.TypeName); // Reflection!
    var instance = Activator.CreateInstance(type);
    // ...
}

// ✅ 正确
// 使用源生成器或静态映射
```

---

### 使用规则 (CAT2xxx)

#### CAT2001: Handler 未注册
**严重性**: Info

Handler 已定义但未注册到 DI 容器。

```csharp
// Handler 定义
public class MyHandler : IRequestHandler<MyCommand, bool> { }

// ✅ 需要注册
services.AddHandler<MyCommand, bool, MyHandler>();
// 或
services.AddGeneratedHandlers();
```

#### CAT2002: 消息没有 Handler
**严重性**: Warning

发送的消息没有对应的 Handler。

```csharp
// ❌ 错误
await mediator.SendAsync(new MyCommand()); // No handler registered

// ✅ 正确
// 1. 实现 Handler
public class MyHandler : IRequestHandler<MyCommand, bool> { }

// 2. 注册 Handler
services.AddHandler<MyCommand, bool, MyHandler>();
```

#### CAT2003: Request 有多个 Handler
**严重性**: Error

`IRequest<T>` 只能有一个 Handler。如需多个处理器，使用 `INotification`。

```csharp
// ❌ 错误
public class Handler1 : IRequestHandler<MyCommand, bool> { }
public class Handler2 : IRequestHandler<MyCommand, bool> { } // Duplicate!

// ✅ 正确：使用 INotification
public record MyEvent : INotification;
public class Handler1 : INotificationHandler<MyEvent> { }
public class Handler2 : INotificationHandler<MyEvent> { }
```

---

### 设计规则 (CAT3xxx)

#### CAT3001: Command 不应返回领域数据
**严重性**: Info

Command 应修改状态并返回最少数据（void, bool, ID）。查询数据使用 Query。

```csharp
// ⚠️ 不推荐
public record CreateUserCommand(string Name) : IRequest<User>; // Returns full entity

// ✅ 推荐
public record CreateUserCommand(string Name) : IRequest<Guid>; // Returns only ID
public record GetUserQuery(Guid Id) : IRequest<User>; // Use Query for data
```

#### CAT3002: Query 应该不可变
**严�性**: Info

Query 表示只读操作，应该是不可变的。

```csharp
// ❌ 错误
public class GetUserQuery : IRequest<User>
{
    public Guid UserId { get; set; } // Mutable!
}

// ✅ 正确
public record GetUserQuery(Guid UserId) : IRequest<User>; // Immutable record
```

#### CAT3003: Event 应使用过去式
**严重性**: Info

Event 表示已发生的事件，命名应使用过去式。

```csharp
// ❌ 错误
public record CreateUserEvent : INotification; // Present tense
public record DeleteOrderEvent : INotification;

// ✅ 正确
public record UserCreatedEvent : INotification; // Past tense
public record OrderDeletedEvent : INotification;
```

---

### 序列化规则 (CAT4xxx)

#### CAT4001: 缺少 MemoryPackable 属性
**严重性**: Info (默认关闭)

为获得最佳 AOT 序列化性能，使用 `[MemoryPackable]`。

```csharp
// ⚠️ 基础
public record MyCommand(string Name) : IRequest<bool>;

// ✅ 最佳（AOT + 性能）
[MemoryPackable]
public partial record MyCommand(string Name) : IRequest<bool>;
```

#### CAT4002: 属性不可序列化
**严重性**: Warning

消息的所有属性都应该可序列化。

```csharp
// ❌ 错误
public record MyCommand(Stream Data) : IRequest<bool>; // Stream not serializable

// ✅ 正确
public record MyCommand(byte[] Data) : IRequest<bool>; // byte[] is serializable
```

---

## 🔧 配置

在 `.editorconfig` 中配置规则：

```ini
# 禁用特定规则
dotnet_diagnostic.CAT3001.severity = none

# 启用可选规则
dotnet_diagnostic.CAT4001.severity = warning

# 调整严重性
dotnet_diagnostic.CAT1002.severity = error
```

---

## 📖 最佳实践

### 推荐的配置

```ini
# .editorconfig

# 性能规则 - 保持启用
dotnet_diagnostic.CAT1002.severity = warning
dotnet_diagnostic.CAT1003.severity = warning

# 使用规则 - 强制执行
dotnet_diagnostic.CAT2003.severity = error

# 设计规则 - 根据团队决定
dotnet_diagnostic.CAT3001.severity = suggestion
dotnet_diagnostic.CAT3002.severity = suggestion
dotnet_diagnostic.CAT3003.severity = suggestion

# 序列化规则 - AOT 项目启用
dotnet_diagnostic.CAT4001.severity = warning  # Native AOT 项目
```

### 在 CI/CD 中使用

```bash
# 构建时检查所有警告
dotnet build -warnaserror

# 只检查 Catga 分析器
dotnet build -warnaserror:CAT1002,CAT1003,CAT2003
```

---

## 📊 统计

运行分析器统计：

```bash
# 查看项目中的所有诊断
dotnet build /p:TreatWarningsAsErrors=false | findstr "CAT"

# 生成报告
dotnet build /flp:Verbosity=diagnostic
```

---

## 🤝 贡献

欢迎贡献新的分析器规则！

**添加新规则的步骤**:
1. 在 `CatgaAnalyzerRules.cs` 中定义规则
2. 创建分析器类（继承 `DiagnosticAnalyzer`）
3. （可选）创建代码修复器（继承 `CodeFixProvider`）
4. 在此文档中添加规则说明
5. 编写单元测试

---

**通过分析器让您的 Catga 代码更健壮、更高效！** 🎯

