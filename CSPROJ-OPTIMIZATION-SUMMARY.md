# csproj 优化总结

## 优化目标
将所有 `.csproj` 文件中重复的配置移到 `Directory.Build.props` 中，简化项目文件，提升可维护性。

## 主要改进

### 1. 根目录 Directory.Build.props 增强

添加了以下自动化配置：

- **默认 TargetFrameworks**: 自动为所有项目（除 SourceGenerator）设置 `net9.0;net8.0`
- **测试项目自动识别**: 通过项目名称或 `IsTestProject` 属性自动设置 `IsPackable=false`
- **通用测试引用**: 为测试项目自动添加 `using Xunit`
- **文档生成控制**: 测试和基准项目自动禁用文档生成

### 2. 新增目录级 Directory.Build.props

#### benchmarks/Directory.Build.props (新建)
```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
```

#### 现有的目录级配置
- **src/Directory.Build.props**: AOT 兼容性配置
- **tests/Directory.Build.props**: 测试项目特定配置
- **examples/Directory.Build.props**: 示例项目配置

### 3. 简化的项目文件

#### 源代码库项目 (src/)
所有库项目从：
```xml
<PropertyGroup>
  <TargetFrameworks>net9.0;net8.0</TargetFrameworks>
  <Description>...</Description>
  <PackageTags>...</PackageTags>
</PropertyGroup>
```

简化为：
```xml
<PropertyGroup>
  <Description>...</Description>
  <PackageTags>...</PackageTags>
</PropertyGroup>
```

#### 测试项目 (tests/)
从：
```xml
<PropertyGroup>
  <TargetFrameworks>net9.0;net8.0</TargetFrameworks>
  <IsPackable>false</IsPackable>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

简化为：
```xml
<PropertyGroup>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

#### 基准测试项目 (benchmarks/)
从：
```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net9.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

简化为：
```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

#### 示例项目 (examples/)
移除了重复的 `Nullable`, `ImplicitUsings`, `TargetFramework` 等配置。

### 4. 跨平台兼容性修复

修复了 `NatsMessageTransport.cs` 中的 `Lock` 类型问题：
```csharp
#if NET9_0_OR_GREATER
    private readonly Lock _jsLock = new();
#else
    private readonly object _jsLock = new();
#endif
```

### 5. 包版本管理优化

- 移除了 `FlowBenchmark.csproj` 中硬编码的 `NSubstitute` 版本号
- 所有包版本统一由 `Directory.Packages.props` 管理

## 优化效果

### 代码减少统计
- **12 个库项目**: 每个减少 1 行 `TargetFrameworks` 配置
- **3 个测试项目**: 每个减少 2-3 行配置
- **4 个示例/基准项目**: 每个减少 2-4 行配置
- **新增**: 1 个 benchmarks/Directory.Build.props 文件
- **总计**: 约 40+ 行重复代码被移除

### 维护性提升
1. **集中管理**: 所有通用配置在一个文件中
2. **层次化配置**: 根目录 → 子目录 → 项目文件的三层配置体系
3. **一致性**: 确保所有项目使用相同的配置
4. **易于更新**: 修改目标框架或其他配置只需改一处

### 构建验证
✅ Debug 构建成功
✅ Release 构建成功  
✅ 支持 net9.0 和 net8.0 双目标框架
✅ 测试项目正确配置
✅ AOT 验证项目正常工作
✅ 基准测试项目正常工作

## 文件变更清单

### 新增文件
- `benchmarks/Directory.Build.props` - 基准测试项目共享配置

### 修改的文件
- `Directory.Build.props` - 增强的根级共享配置
- `src/Catga.Transport.Nats/NatsMessageTransport.cs` - 跨平台兼容性修复
- 所有 `*.csproj` 文件 (19 个) - 移除重复配置

### 影响的项目
- 12 个源代码库项目
- 3 个测试项目  
- 2 个示例项目
- 2 个基准测试项目

## 配置层次结构

```
根目录/Directory.Build.props (全局配置)
├── src/Directory.Build.props (源代码库 AOT 配置)
│   └── 12 个库项目 csproj
├── tests/Directory.Build.props (测试项目配置)
│   └── 3 个测试项目 csproj
├── examples/Directory.Build.props (示例项目配置)
│   └── 2 个示例项目 csproj
└── benchmarks/Directory.Build.props (基准测试配置)
    └── 2 个基准项目 csproj
```

## 后续建议

1. **考虑添加 Directory.Build.targets**: 用于共享的构建后任务
2. **版本管理**: 考虑使用 GitVersion 或 Nerdbank.GitVersioning
3. **代码分析**: 可以在 Directory.Build.props 中添加共享的分析器配置
4. **CI/CD 优化**: 利用层次化配置简化 CI/CD 脚本
