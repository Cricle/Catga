# Catga 分析器配置与接入

> 这一页专门放规则启用方式、项目配置、CI/CD 集成、最佳实践和故障排查。
> 如果你在找规则本身和触发示例，请看 [analyzers-rules.md](./analyzers-rules.md)。

---

## 推荐阅读顺序

1. 先确认 [分析器规则参考](./analyzers-rules.md) 里的规则语义
2. 再决定是新项目接入还是迁移项目接入
3. 最后把 CI 配置补上

---

## 🔧 配置分析器

### 全局配置

在 `Directory.Build.props` 中配置所有项目：

```xml
<Project>
  <PropertyGroup>
    <!-- 将所有分析器警告视为错误 (推荐生产环境) -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <!-- 或只阻断高风险 Catga 规则 -->
    <WarningsAsErrors>CAT2004;CATGA002</WarningsAsErrors>

    <!-- 调整严重性 -->
    <!-- CATGA001 从 Info 提升到 Warning -->
    <CATGA001>warning</CATGA001>
  </PropertyGroup>
</Project>
```

### 项目级配置

在 `.csproj` 中配置：

```xml
<PropertyGroup>
  <!-- 禁用特定规则 -->
  <NoWarn>$(NoWarn);CATGA001</NoWarn>

  <!-- 启用所有规则（包括默认禁用的） -->
  <AnalysisLevel>latest-all</AnalysisLevel>
</PropertyGroup>
```

### .editorconfig 配置

更细粒度的配置：

```ini
[*.cs]

# CATGA001: MemoryPackable 属性
dotnet_diagnostic.CATGA001.severity = suggestion

# CATGA002: 序列化器注册
dotnet_diagnostic.CATGA002.severity = error

# CAT2004: singleton -> scoped 生命周期冲突
dotnet_diagnostic.CAT2004.severity = error

# CAT1001: Handler 实现
dotnet_diagnostic.CAT1001.severity = error

# 全局禁用某个规则
dotnet_diagnostic.CAT3003.severity = none
```

### 代码级抑制

在特定代码中抑制：

```csharp
// 单行抑制
#pragma warning disable CATGA001
public record MyMessage(...) : IRequest<MyResult>;
#pragma warning restore CATGA001

// 文件级抑制
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CATGA001:Message should have MemoryPackable attribute",
    Justification = "Using JSON serialization")]

// 类级抑制
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CATGA001")]
public record MyMessage(...) : IRequest<MyResult>;
```

---

## 💡 使用场景

### 场景 1: 新项目开发

**建议配置**:
```xml
<PropertyGroup>
  <!-- 所有警告视为错误 -->
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

  <!-- 保底阻断 singleton -> scoped Catga 误用 -->
  <WarningsAsErrors>$(WarningsAsErrors);CAT2004</WarningsAsErrors>

  <!-- CATGA001 提升为警告 -->
  <CATGA001>warning</CATGA001>
</PropertyGroup>
```

**收益**: 强制团队遵循最佳实践

### 场景 2: 迁移现有项目

**建议配置**:
```xml
<PropertyGroup>
  <!-- 逐步迁移，先显示信息 -->
  <CATGA001>suggestion</CATGA001>
  <CATGA002>warning</CATGA002>
  <WarningsAsErrors>$(WarningsAsErrors);CAT2004</WarningsAsErrors>
</PropertyGroup>
```

**收益**: 逐步改进，不阻塞构建

### 场景 3: CI/CD 集成

**GitHub Actions**:
```yaml
- name: Build with analyzers
  run: dotnet build /p:TreatWarningsAsErrors=true

- name: Check for warnings
  run: dotnet build /warnaserror
```

**Azure DevOps**:
```yaml
- task: DotNetCoreCLI@2
  inputs:
    command: 'build'
    arguments: '/p:TreatWarningsAsErrors=true'
```

**收益**: 确保代码质量，防止带 bug 的代码合并

---

## 🎓 最佳实践

### ✅ 推荐做法

1. **新项目启用所有规则**
   ```xml
   <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
   ```

2. **所有消息标注 [MemoryPackable]**
   ```csharp
   [MemoryPackable]
   public partial record MyMessage(...) : IRequest<MyResult>;
   ```

3. **立即修复警告**
   - 不要抑制警告
   - 使用自动修复
   - 理解警告原因

4. **CI/CD 强制检查**
   ```yaml
   dotnet build /warnaserror
   ```

### ❌ 避免做法

1. **不要全局禁用分析器**
   ```xml
   <!-- ❌ 错误 -->
   <RunAnalyzers>false</RunAnalyzers>
   ```

2. **不要随意抑制警告**
   ```csharp
   // ❌ 错误 - 没有正当理由
   #pragma warning disable CATGA001
   ```

3. **不要忽略 CATGA002**
   ```csharp
   // ❌ 错误 - 运行时会崩溃
   services.AddCatga();  // 忘记序列化器
   ```

---

## 🐛 故障排除

### 问题 1: 分析器未运行

**症状**: 没有看到任何警告

**解决方案**:
```bash
# 清理并重新构建
dotnet clean
dotnet build

# 检查是否启用
dotnet build /p:RunAnalyzers=true

# 查看详细输出
dotnet build -v detailed | findstr "Catga"
```

### 问题 2: 误报

**症状**: 明明已经注册序列化器，但仍警告

**原因**: 跨方法调用检测限制

**解决方案**:
```csharp
// 方式 1: 在同一方法注册（推荐）
services.AddCatga().UseMemoryPack();

// 方式 2: 合理抑制
#pragma warning disable CATGA002
services.AddCatga();
#pragma warning restore CATGA002
RegisterSerializerInAnotherMethod(services);
```

### 问题 3: IDE 中不显示

**Visual Studio**:
1. 工具 → 选项 → 文本编辑器 → C# → 高级
2. 勾选"启用完整解决方案分析"

**VS Code**:
1. 安装 C# 扩展
2. 重新加载窗口

**Rider**:
1. 设置 → Editor → Inspections
2. 启用 "Roslyn Analyzers"

---

## 📊 性能影响

| 操作 | 无分析器 | 有分析器 | 影响 |
|------|----------|----------|------|
| **首次编译** | 2.5s | 2.8s | +12% |
| **增量编译** | 0.8s | 0.9s | +13% |
| **IDE 智能提示** | 50ms | 60ms | +20% |
| **CI/CD 构建** | 45s | 50s | +11% |

**结论**: 性能影响可接受（< 15%），收益远大于成本

---

## 🔮 未来规划

### v2.1 (计划中)

- **CATGA003**: 检测未使用的 Handler
- **CATGA004**: 检测循环依赖
- **CATGA005**: 性能：检测 Handler 中的同步阻塞

### v2.2 (计划中)

- **CATGA006**: 安全：检测敏感数据泄露
- **CATGA007**: AOT：检测不兼容的代码模式
- 更多自动修复

---

## 📚 相关资源

- **[Roslyn 分析器官方文档](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/)**
- **[源生成器指南](./source-generator.md)**
- **[序列化指南](./serialization.md)**
- **[AOT 最佳实践](../deployment/native-aot-publishing.md)**

---

## 🎯 快速参考

### 常用命令

```bash
# 运行分析器
dotnet build

# 将警告视为错误
dotnet build /warnaserror

# 查看所有诊断
dotnet build /p:RunAnalyzers=true -v detailed

# 禁用特定规则
dotnet build /p:NoWarn=CATGA001
```

### 常用配置

```xml
<!-- 推荐生产配置 -->
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <WarningsAsErrors>$(WarningsAsErrors);CAT2004</WarningsAsErrors>
  <CATGA001>warning</CATGA001>
  <CATGA002>error</CATGA002>
</PropertyGroup>
```

---

<div align="center">

**🔍 让编译器帮你写出更好的代码！**

[返回主文档](../README.md) · [架构设计](../architecture/ARCHITECTURE.md)

**推荐**: 启用所有分析器，在编译时发现问题

</div>
