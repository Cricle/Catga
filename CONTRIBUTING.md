# 贡献指南

感谢你对 Catga 项目的兴趣！我们欢迎各种形式的贡献。

## 🤝 如何贡献

### 报告 Bug

如果你发现了 bug，请：

1. 检查 [Issues](https://github.com/YOUR_USERNAME/Catga/issues) 中是否已有相同问题
2. 如果没有，创建新的 Issue，包含：
   - 清晰的标题
   - 详细的问题描述
   - 重现步骤
   - 预期行为 vs 实际行为
   - 环境信息（.NET 版本、OS 等）
   - 相关代码示例或日志

### 提出新功能

如果你有新功能的想法：

1. 先创建一个 Issue 讨论
2. 说明功能的用例和价值
3. 等待维护者反馈
4. 获得批准后再开始开发

### 提交代码

1. **Fork 项目**
   ```bash
   # 点击 GitHub 上的 Fork 按钮
   ```

2. **克隆你的 Fork**
   ```bash
   git clone https://github.com/YOUR_USERNAME/Catga.git
   cd Catga
   ```

3. **创建特性分支**
   ```bash
   git checkout -b feature/amazing-feature
   ```

4. **进行更改**
   - 编写代码
   - 添加测试
   - 更新文档

5. **提交更改**
   ```bash
   git add .
   git commit -m "feat: Add amazing feature"
   ```

6. **推送到你的 Fork**
   ```bash
   git push origin feature/amazing-feature
   ```

7. **创建 Pull Request**
   - 在 GitHub 上打开 PR
   - 填写 PR 模板
   - 等待 Review

## 📝 代码规范

### C# 编码风格

我们使用 `.editorconfig` 定义的编码规范：

```csharp
// ✅ 推荐
public class MyClass
{
    private readonly IService _service;

    public MyClass(IService service)
    {
        _service = service;
    }

    public async Task<Result> DoSomethingAsync(string input)
    {
        // 实现
    }
}
```

### 命名约定

- **类名**: PascalCase
- **接口**: IPascalCase (以 I 开头)
- **方法**: PascalCase
- **参数**: camelCase
- **私有字段**: _camelCase (以下划线开头)
- **常量**: UPPER_CASE

### 提交消息格式

使用 [Conventional Commits](https://www.conventionalcommits.org/)：

```
<type>(<scope>): <subject>

<body>

<footer>
```

**类型**:
- `feat`: 新功能
- `fix`: Bug 修复
- `docs`: 文档更改
- `style`: 代码格式（不影响功能）
- `refactor`: 重构
- `test`: 添加或修改测试
- `chore`: 构建过程或辅助工具的变动
- `perf`: 性能优化

**示例**:

```
feat(mediator): Add support for async event handlers

- Implement IAsyncEventHandler interface
- Update PublishAsync to handle async handlers
- Add tests for async event handling

Closes #123
```

## 🧪 测试要求

### 单元测试

- 所有新功能必须有单元测试
- 测试覆盖率应 ≥ 80%
- 使用 xUnit, FluentAssertions, NSubstitute

```csharp
[Fact]
public async Task SendAsync_WithValidCommand_ShouldReturnSuccess()
{
    // Arrange
    var command = new TestCommand { Value = "test" };

    // Act
    var result = await _mediator.SendAsync<TestCommand, TestResponse>(command);

    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.Should().NotBeNull();
}
```

### 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定测试
dotnet test --filter "FullyQualifiedName~CatgaMediatorTests"

# 生成覆盖率报告
dotnet test /p:CollectCoverage=true
```

## 📚 文档要求

### API 文档

- 所有公共 API 必须有 XML 文档注释
- 包含描述、参数说明、返回值、示例

```csharp
/// <summary>
/// 发送请求并等待响应
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
/// <param name="request">请求对象</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>包含响应的结果</returns>
/// <example>
/// <code>
/// var command = new CreateOrderCommand { ProductId = "PROD-001" };
/// var result = await mediator.SendAsync&lt;CreateOrderCommand, OrderResult&gt;(command);
/// </code>
/// </example>
public Task<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest<TResponse>;
```

### README 更新

- 如果添加新功能，更新 README.md
- 添加使用示例
- 更新功能列表

## 🔍 代码审查流程

### Pull Request 检查清单

- [ ] 代码遵循项目规范
- [ ] 所有测试通过
- [ ] 添加了必要的测试
- [ ] 更新了相关文档
- [ ] 提交消息符合规范
- [ ] 没有引入破坏性更改（如有，请在 PR 中说明）

### 审查标准

1. **代码质量**
   - 可读性
   - 可维护性
   - 性能
   - 安全性

2. **测试覆盖**
   - 单元测试
   - 集成测试（如适用）
   - 边界情况

3. **文档完整性**
   - XML 文档注释
   - README 更新
   - API 文档

## 🚀 发布流程

### 版本号规范

使用 [语义化版本](https://semver.org/lang/zh-CN/)：

- **主版本号**：不兼容的 API 修改
- **次版本号**：向后兼容的功能性新增
- **修订号**：向后兼容的问题修正

### 发布步骤

1. 更新 `CHANGELOG.md`
2. 创建版本标签
   ```bash
   git tag -a v1.0.0 -m "Release v1.0.0"
   git push origin v1.0.0
   ```
3. GitHub Actions 自动构建和发布

## 💡 开发建议

### 本地开发设置

```bash
# 克隆项目
git clone https://github.com/YOUR_USERNAME/Catga.git
cd Catga

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行测试
dotnet test

# 运行基准测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

### 推荐工具

- **IDE**: Visual Studio 2022, JetBrains Rider, VS Code
- **扩展**:
  - C# Dev Kit (VS Code)
  - ReSharper (Visual Studio)
  - CodeMaid
- **工具**:
  - dotnet format
  - BenchmarkDotNet
  - Coverlet

### 调试技巧

```csharp
// 使用条件断点
Debug.Assert(condition, "Error message");

// 使用日志
_logger.LogDebug("Processing request: {RequestId}", request.MessageId);

// 使用 DiagnosticSource
Activity.Current?.AddTag("request.type", typeof(TRequest).Name);
```

## 📞 联系方式

- **Issues**: [GitHub Issues](https://github.com/YOUR_USERNAME/Catga/issues)
- **Discussions**: [GitHub Discussions](https://github.com/YOUR_USERNAME/Catga/discussions)
- **Email**: your-email@example.com

## 📄 许可证

通过贡献代码，你同意你的贡献将在 MIT 许可证下授权。

---

再次感谢你的贡献！🎉

