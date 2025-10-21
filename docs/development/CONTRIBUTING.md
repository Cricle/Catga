# 贡献指南

感谢您考虑为 Catga 做出贡献！🎉

## 🎯 贡献方式

我们欢迎以下类型的贡献：

- 🐛 **Bug 报告和修复**
- ✨ **新功能建议和实现**
- 📖 **文档改进**
- 🧪 **测试用例**
- 💡 **性能优化**
- 🌐 **翻译**

## 🚀 快速开始

### 1. Fork 和 Clone

```bash
# Fork 项目到你的账号
# 然后 Clone 到本地
git clone https://github.com/YOUR_USERNAME/Catga.git
cd Catga
```

### 2. 创建分支

```bash
git checkout -b feature/your-feature-name
# 或
git checkout -b fix/your-bug-fix
```

### 3. 开发环境

**要求**:
- .NET 9.0 SDK 或更高
- IDE: Visual Studio 2022 / Rider / VS Code

**编译**:
```bash
dotnet build Catga.sln
```

**运行测试**:
```bash
dotnet test
```

### 4. 提交更改

```bash
git add .
git commit -m "feat: add awesome feature"
git push origin feature/your-feature-name
```

### 5. 创建 Pull Request

在 GitHub 上创建 Pull Request，描述你的更改。

## 📝 提交规范

我们使用 [Conventional Commits](https://www.conventionalcommits.org/) 规范：

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Type 类型

- `feat`: 新功能
- `fix`: Bug 修复
- `docs`: 文档更新
- `style`: 代码格式（不影响功能）
- `refactor`: 重构（不是新功能也不是修复）
- `perf`: 性能优化
- `test`: 测试相关
- `chore`: 构建过程或辅助工具的变动

### 示例

```
feat(mediator): add batch processing support

- Add BatchRequest interface
- Implement batch handler registration
- Add unit tests for batch processing

Closes #123
```

## 🎨 代码规范

### C# 代码风格

1. **使用现代 C# 特性**
   ```csharp
   // ✅ 推荐
   public record CreateOrderCommand(string OrderId, decimal Amount);

   // ❌ 避免
   public class CreateOrderCommand
   {
       public string OrderId { get; set; }
       public decimal Amount { get; set; }
   }
   ```

2. **简洁的代码**
   ```csharp
   // ✅ 推荐
   public string GetName() => _name ?? "Unknown";

   // ❌ 避免不必要的冗长
   public string GetName()
   {
       if (_name != null)
           return _name;
       else
           return "Unknown";
   }
   ```

3. **AOT 友好**
   ```csharp
   // ✅ 推荐 - 使用泛型缓存
   TypeNameCache<T>.Name

   // ❌ 避免 - 热路径反射
   typeof(T).Name
   ```

### 性能考虑

1. **避免分配**
   ```csharp
   // ✅ 推荐
   public ValueTask<Result> Handle(...) => ValueTask.FromResult(...);

   // ❌ 避免不必要的 Task 分配
   public Task<Result> Handle(...) => Task.FromResult(...);
   ```

2. **使用 Span<T>**
   ```csharp
   // ✅ 推荐
   public void Process(ReadOnlySpan<byte> data) { }

   // ❌ 避免不必要的数组
   public void Process(byte[] data) { }
   ```

3. **对象池**
   ```csharp
   // ✅ 推荐 - 复用对象
   var buffer = ArrayPool<byte>.Shared.Rent(size);
   try { /* use buffer */ }
   finally { ArrayPool<byte>.Shared.Return(buffer); }
   ```

### 注释规范

1. **XML 文档注释**
   ```csharp
   /// <summary>Process order command (AOT-friendly)</summary>
   /// <param name="command">Order command</param>
   /// <returns>Processing result</returns>
   public Task<Result> ProcessAsync(OrderCommand command);
   ```

2. **简洁英文**
   ```csharp
   // ✅ 推荐 - 简短英文
   // Cache type name for performance

   // ❌ 避免 - 冗长或中文
   // 这里我们缓存类型名称以提高性能避免反射调用
   ```

3. **只在必要时注释**
   - 复杂算法需要注释
   - 性能优化需要说明
   - 不要为显而易见的代码写注释

## 🧪 测试要求

### 单元测试

所有新功能必须有单元测试：

```csharp
public class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        // Arrange
        var handler = new CreateOrderHandler();
        var command = new CreateOrderCommand("ORD-001", 99.99m);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ORD-001", result.Data.OrderId);
    }
}
```

### 性能测试

性能关键代码需要基准测试：

```csharp
[MemoryDiagnoser]
public class MyBenchmark
{
    [Benchmark(Baseline = true)]
    public void OldImplementation() { }

    [Benchmark]
    public void NewImplementation() { }
}
```

运行基准测试：
```bash
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

### 测试覆盖率

- 核心功能：80%+ 覆盖率
- 新功能：70%+ 覆盖率
- 关键路径：90%+ 覆盖率

## 📖 文档要求

### 代码文档

1. **公开 API 必须有 XML 注释**
2. **复杂算法需要说明**
3. **性能敏感代码需要注明**

### 用户文档

新功能需要更新文档：

1. **README.md** - 主要特性
2. **QUICK-REFERENCE.md** - 快速参考
3. **docs/** - 详细指南

文档格式：
- 清晰的标题层次
- 代码示例
- 使用场景
- 最佳实践
- 常见问题

## 🔍 Code Review 流程

### Pull Request 检查清单

提交 PR 前请确认：

- [ ] 代码遵循项目规范
- [ ] 所有测试通过
- [ ] 新功能有单元测试
- [ ] 文档已更新
- [ ] 提交信息符合规范
- [ ] 无编译警告
- [ ] AOT 兼容（如果修改核心代码）

### Review 标准

我们会检查：

1. **代码质量**
   - 是否遵循最佳实践
   - 是否有性能问题
   - 是否 AOT 兼容

2. **测试完整性**
   - 测试覆盖率
   - 测试质量
   - 边界情况

3. **文档完整性**
   - API 文档
   - 用户文档
   - 示例代码

## 💡 开发提示

### 性能优化原则

1. **测量先行**: 使用 BenchmarkDotNet 测量
2. **避免过早优化**: 先保证正确性
3. **关注热路径**: 优化高频调用的代码
4. **零分配目标**: 热路径尽量零分配

### AOT 兼容性检查

```bash
# 发布 AOT 版本测试
dotnet publish -c Release -r win-x64 /p:PublishAot=true

# 检查警告
# 不应该有 IL2026 或 IL3050 警告（核心库）
```

### 调试技巧

1. **使用 Benchmark**
   ```csharp
   [Benchmark]
   [Arguments(1000)]
   public void TestMethod(int iterations) { }
   ```

2. **使用 Memory Profiler**
   - Visual Studio Diagnostic Tools
   - dotMemory
   - PerfView

3. **查看生成的代码**
   ```bash
   # 查看 IL 代码
   ildasm YourAssembly.dll

   # 查看源生成器输出
   # 在 obj/Debug/net9.0/generated/ 目录
   ```

## 🏆 成为贡献者

贡献被接受后，你将：

- ✅ 被添加到贡献者列表
- ✅ 获得贡献者徽章
- ✅ 参与项目决策（活跃贡献者）

### 活跃贡献者权益

持续贡献的开发者可以：

- 🎯 参与新特性讨论
- 📊 访问性能数据
- 🔍 提前测试新版本
- 💬 加入核心团队 Discord

## 📞 联系方式

有问题？

- 💬 GitHub Issues
- 📧 Email: [project email]
- 💭 Discord: [server link]

## 🙏 致谢

感谢每一位贡献者！你们让 Catga 变得更好。

---

**开始贡献吧！我们期待你的 Pull Request！** 🚀
