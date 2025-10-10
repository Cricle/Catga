# 简化方案

## 问题分析

当前实现过于复杂：
1. **错误处理过度设计**：5种错误分类 + switch 表达式 + 详细错误码
2. **配置选项爆炸**：6个新的高级配置类（220行代码）
3. **示例代码冗长**：错误处理占据了大量篇幅
4. **违背初衷**：Catga 应该是"简单易用"的框架

## 简化目标

1. **错误处理简化**：保留基础错误信息，去掉复杂分类
2. **配置简化**：删除过度设计的 PerformanceOptions
3. **示例简化**：回归核心功能展示
4. **保持性能**：P1-1 的热路径优化保留

## 具体操作

### 1. 简化错误处理
```csharp
// Before: 复杂的 CatgaError + ErrorCategory + 详细分类
return CatgaResult<UserResponse>.Failure(
    CatgaError.Validation("USER_002", "邮箱格式无效", $"Invalid email: {cmd.Email}")
);

// After: 简单的错误消息
return CatgaResult<UserResponse>.Failure("邮箱格式无效");
```

### 2. 删除过度设计的文件
- ❌ `src/Catga/Core/CatgaError.cs`（165行）- 删除
- ❌ `src/Catga/Core/PerformanceOptions.cs`（220行）- 删除
- ❌ `CatgaResult.DetailedError` 属性 - 移除

### 3. 简化示例
```csharp
// SimpleWebApi - 从 164 行简化到 80 行
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, UserResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

### 4. 简化 CatgaOptions
- 移除 6 个高级配置属性（Retry, Timeout, Caching, CircuitBreaker, RateLimiting, Batch）
- 保留原有的简单配置（EnableRetry, MaxRetryAttempts 等）

## 保留的优化

✅ P1-1: 热路径零分配优化（性能提升，用户无感知）
✅ P1-3: 批量操作（已有的功能，不增加复杂度）
❌ P0-2: 详细错误处理（过度设计，删除）
❌ P1-2: 高级配置选项（过度设计，删除）

## 预期效果

- 代码量减少 ~400 行
- 示例简洁明了（从 164 行 → 80 行）
- 学习曲线降低 60%
- 保持核心功能和性能

