# Catga 示例

简洁的示例，展示 Catga 的核心功能。

## 📚 示例列表

### 基础示例

| 示例 | 说明 | 代码行数 | 运行 |
|------|------|----------|------|
| [01-HelloWorld](./01-HelloWorld/) | 最简单的示例 | ~25 行 | `dotnet run` |
| [02-CQRS-Basic](./02-CQRS-Basic/) | CQRS 模式完整演示 | ~80 行 | `dotnet run` |
| [03-Pipeline](./03-Pipeline/) | 中间件和 Pipeline | ~65 行 | `dotnet run` |
| [04-NativeAOT](./04-NativeAOT/) | Native AOT 发布 | ~35 行 | `dotnet publish` |

### 高级示例

| 示例 | 说明 | 特性 |
|------|------|------|
| [OrderSystem](./OrderSystem/) | 完整订单系统 | EF Core, Redis, NATS |
| [MicroservicesDemo](./MicroservicesDemo/) | 微服务 RPC 调用 | 跨服务调用 |

## 🚀 快速开始

### 运行基础示例

```bash
# HelloWorld
cd examples/01-HelloWorld
dotnet run

# CQRS
cd examples/02-CQRS-Basic
dotnet run

# Pipeline
cd examples/03-Pipeline
dotnet run
```

### Native AOT 示例

```bash
cd examples/04-NativeAOT

# 发布
dotnet publish -c Release -r win-x64

# 运行（超快启动）
./bin/Release/net9.0/win-x64/publish/NativeAOT.exe
```

## 📖 学习路径

### 第 1 天 - 基础

1. **HelloWorld** - 了解基本用法 (5分钟)
2. **CQRS-Basic** - 理解 CQRS 模式 (15分钟)
3. **Pipeline** - 掌握中间件 (10分钟)

### 第 2 天 - 进阶

4. **NativeAOT** - 体验极致性能 (10分钟)
5. **OrderSystem** - 学习实际应用 (30分钟)

### 第 3 天 - 分布式

6. **MicroservicesDemo** - 微服务架构 (30分钟)

## 💡 示例特点

### ✅ 简洁
- 每个示例 < 100 行代码
- 单文件结构
- 专注核心功能

### ✅ 实用
- 真实场景
- 最佳实践
- 可直接复制使用

### ✅ 渐进式
- 从简单到复杂
- 循序渐进
- 易于理解

## 🎯 代码风格

所有示例遵循：

```csharp
// ✅ 简洁的 Record
public record MyCommand(string Name) : IRequest<bool>;

// ✅ 简短的 Handler
public class MyHandler : IRequestHandler<MyCommand, bool>
{
    public Task<CatgaResult<bool>> Handle(...)
        => Task.FromResult(CatgaResult<bool>.Success(true));
}

// ✅ 流畅的配置
services.AddCatga()
    .AddHandler<MyCommand, bool, MyHandler>();
```

## 📊 性能对比

运行 NativeAOT 示例查看差异：

| 指标 | 传统 .NET | Native AOT |
|------|-----------|------------|
| 启动时间 | ~1200ms | **~50ms** |
| 文件大小 | ~68MB | **~8MB** |
| 内存占用 | ~85MB | **~12MB** |

## 🤝 贡献

欢迎贡献新示例！

要求：
- < 100 行代码
- 单文件或最少文件
- 清晰的注释
- README 说明

## 📚 更多资源

- [完整文档](../README.md)
- [快速参考](../QUICK-REFERENCE.md)
- [性能优化](../REFLECTION_OPTIMIZATION_SUMMARY.md)
- [Native AOT 指南](../docs/deployment/native-aot-publishing.md)

---

**从第一个示例开始学习 Catga！** 🚀
