# 清理和服务自动发现计划

## 📋 执行计划

### 阶段 1: 清理无用的注释和文档 ✅

#### 1.1 审查代码注释
**目标**: 删除过时、冗余、无价值的注释

**需要清理的注释类型**:
- [ ] 自动生成的默认注释（如 "// TODO: ..."）
- [ ] 过时的设计说明
- [ ] 重复代码的注释
- [ ] 显而易见的注释（如 `// Set x to 1` 对应 `x = 1;`）
- [ ] 调试用的临时注释
- [ ] 中英文混杂的注释（统一为中文）

**保留的注释类型**:
- ✅ XML 文档注释（`///`）- 用于 IntelliSense
- ✅ 架构设计说明
- ✅ 性能优化说明
- ✅ 无锁设计说明
- ✅ 复杂算法说明

**执行步骤**:
1. 扫描所有 `.cs` 文件中的注释
2. 识别并删除无用注释
3. 统一注释风格
4. 确保 XML 文档完整

---

### 阶段 2: 实现服务自动发现 🔄

#### 2.1 需求分析

**功能目标**:
- 自动扫描程序集
- 自动注册 Handler
- 自动注册 Pipeline Behavior
- 自动注册 Validator
- 减少手动配置

**设计原则**:
- AOT 兼容（使用 Source Generator）
- 零反射（编译时生成）
- 高性能
- 类型安全

#### 2.2 实现方案

##### 方案 1: Source Generator（推荐）✅
**优点**:
- 完全 AOT 兼容
- 编译时生成代码
- 零运行时开销
- 类型安全

**实现**:
```csharp
// 1. 创建 Attribute 标记
[AttributeUsage(AttributeTargets.Class)]
public class AutoRegisterAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
}

// 2. 使用标记
[AutoRegister(Lifetime = ServiceLifetime.Transient)]
public class MyHandler : IRequestHandler<MyRequest, MyResponse>
{
    // ...
}

// 3. Source Generator 生成注册代码
// Generated: AutoRegistrationExtensions.g.cs
public static class AutoRegistrationExtensions
{
    public static IServiceCollection AddAutoDiscoveredServices(this IServiceCollection services)
    {
        // 自动生成的注册代码
        services.AddTransient<IRequestHandler<MyRequest, MyResponse>, MyHandler>();
        // ...
        return services;
    }
}

// 4. 使用
services.AddCatga();
services.AddAutoDiscoveredServices(); // 一行自动注册所有服务
```

##### 方案 2: 约定优于配置
**优点**:
- 无需标记
- 更简洁

**实现**:
```csharp
// 自动扫描所有实现 IRequestHandler/IEventHandler 的类
// Source Generator 在编译时扫描并生成注册代码
public static class AutoRegistrationExtensions
{
    public static IServiceCollection AddCatgaHandlers(this IServiceCollection services)
    {
        // 自动生成的代码
        services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserHandler>();
        services.AddTransient<IEventHandler<UserCreated>, UserCreatedHandler>();
        // ...
        return services;
    }
}
```

#### 2.3 实现步骤

**Step 1: 创建 Source Generator 项目**
```
src/Catga.SourceGenerator.AutoDiscovery/
├── AutoDiscoveryGenerator.cs
├── AutoRegisterAttribute.cs
└── Templates/
    └── AutoRegistrationExtensions.template
```

**Step 2: 实现 Source Generator**
```csharp
[Generator]
public class AutoDiscoveryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. 查找所有实现 Handler 接口的类
        // 2. 生成注册代码
        // 3. 输出到 AutoRegistrationExtensions.g.cs
    }
}
```

**Step 3: 生成注册代码**
```csharp
// AutoRegistrationExtensions.g.cs (自动生成)
public static partial class CatgaAutoDiscoveryExtensions
{
    public static IServiceCollection AddCatgaHandlers(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        // Request Handlers
        services.Add(new ServiceDescriptor(
            typeof(IRequestHandler<GetUserQuery, User>),
            typeof(GetUserHandler),
            lifetime));
        
        // Event Handlers
        services.Add(new ServiceDescriptor(
            typeof(IEventHandler<UserCreated>),
            typeof(UserCreatedHandler),
            lifetime));
        
        // Pipeline Behaviors (如果有)
        
        return services;
    }
}
```

**Step 4: 使用示例**
```csharp
// 之前：手动注册每个 Handler
services.AddTransient<IRequestHandler<GetUserQuery, User>, GetUserHandler>();
services.AddTransient<IRequestHandler<CreateUserCommand, int>, CreateUserHandler>();
services.AddTransient<IEventHandler<UserCreated>, SendWelcomeEmailHandler>();
// ... 数十个注册

// 之后：自动发现和注册
services.AddCatga();
services.AddCatgaHandlers(); // 一行搞定！
```

---

### 阶段 3: 更新文档和示例 📚

#### 3.1 更新 README
- [ ] 添加服务自动发现章节
- [ ] 更新快速开始示例
- [ ] 添加 Source Generator 说明

#### 3.2 更新示例项目
- [ ] RedisExample 使用自动发现
- [ ] 演示手动注册 vs 自动发现对比

#### 3.3 创建迁移指南
- [ ] 从手动注册迁移到自动发现
- [ ] 性能对比
- [ ] 最佳实践

---

## 🎯 优先级

### P0 (立即执行)
1. ✅ 清理明显无用的注释
2. ✅ 统一注释风格

### P1 (本周完成)
3. 🔄 实现 Source Generator 自动发现
4. 🔄 更新示例使用自动发现

### P2 (可选)
5. ⏸️ 创建迁移指南
6. ⏸️ 性能基准测试

---

## 📊 预期效果

### 清理注释
- **减少代码行数**: ~10-15%
- **提高可读性**: 更清晰
- **减少维护成本**: 更容易理解

### 服务自动发现
- **减少配置代码**: 90%+
- **减少人为错误**: 忘记注册 Handler
- **提高开发效率**: 专注业务逻辑
- **保持 AOT 兼容**: 编译时生成

**示例对比**:
```csharp
// 手动注册：50 行
services.AddTransient<IRequestHandler<Query1, Response1>, Handler1>();
services.AddTransient<IRequestHandler<Query2, Response2>, Handler2>();
// ... 48 more lines

// 自动发现：1 行
services.AddCatgaHandlers();
```

---

## 🔧 技术细节

### Source Generator 优势
1. **编译时生成**: 零运行时开销
2. **AOT 兼容**: 无反射
3. **类型安全**: 编译时检查
4. **可调试**: 生成的代码可查看
5. **增量生成**: 快速编译

### 实现难点
1. ❌ **泛型类型识别**: 需要正确解析 `IRequestHandler<TRequest, TResponse>`
2. ❌ **生命周期控制**: 支持 Transient/Scoped/Singleton
3. ❌ **命名空间处理**: 生成正确的 using 语句
4. ✅ **增量编译**: 只在相关文件变化时重新生成

---

## 📅 执行时间线

### Day 1-2: 清理注释
- [x] 扫描和识别无用注释
- [ ] 批量清理
- [ ] 统一风格
- [ ] 验证编译和测试

### Day 3-5: 实现自动发现
- [ ] 创建 Source Generator 项目
- [ ] 实现基础生成逻辑
- [ ] 处理泛型类型
- [ ] 测试生成代码

### Day 6-7: 集成和测试
- [ ] 更新示例项目
- [ ] 性能测试
- [ ] 文档更新
- [ ] 用户验收测试

---

## ✅ 验收标准

### 清理注释
- [ ] 删除所有无用注释
- [ ] XML 文档完整性 > 90%
- [ ] 代码可读性提升
- [ ] 编译和测试通过

### 服务自动发现
- [ ] Source Generator 正常工作
- [ ] 生成的代码编译通过
- [ ] AOT 发布成功
- [ ] 示例项目更新
- [ ] 文档完整

---

## 🚀 下一步行动

1. **审查批准**: 确认计划
2. **执行阶段 1**: 清理注释（1-2小时）
3. **执行阶段 2**: 实现自动发现（4-6小时）
4. **执行阶段 3**: 更新文档（1-2小时）
5. **验收测试**: 确保质量
6. **发布**: 推送到远程仓库

---

**总预计时间**: 1-2 天
**优先级**: P0 (高)
**影响**: 提升开发体验和代码质量

---

准备开始执行吗？

