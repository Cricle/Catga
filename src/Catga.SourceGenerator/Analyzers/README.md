# Catga 分析器实现

此目录包含 Catga 框架的 Roslyn 分析器和代码修复器。

## 📁 结构

```
Analyzers/
├── CatgaAnalyzerRules.cs         # 所有诊断规则定义
├── BlockingCallAnalyzer.cs       # 检测阻塞调用
├── MultipleHandlersAnalyzer.cs   # 检测重复 Handler
├── NamingConventionAnalyzer.cs   # 命名约定检查
├── ScopedLifetimeMismatchAnalyzer.cs # 检测 singleton -> scoped 生命周期误用
└── README.md
```

## 🔨 开发新分析器

### 1. 定义规则

在 `CatgaAnalyzerRules.cs` 中添加：

```csharp
public static readonly DiagnosticDescriptor MyRule = new(
    id: "CAT5001",
    title: "My rule title",
    messageFormat: "Message format with {0}",
    category: Category,
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true,
    description: "Detailed description.");
```

### 2. 实现分析器

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(CatgaAnalyzerRules.MyRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        // Analysis logic
        var diagnostic = Diagnostic.Create(
            CatgaAnalyzerRules.MyRule,
            location,
            args);
        context.ReportDiagnostic(diagnostic);
    }
}
```


## 🧪 测试

手动测试分析器：

```csharp
// TestProject/TestCode.cs

// This should trigger CAT1002
public async Task<CatgaResult<bool>> BadHandler(MyCommand cmd, CancellationToken ct)
{
    var result = _service.GetDataAsync().Result; // ❌ Blocking call
    return CatgaResult<bool>.Success(result);
}
```

编译项目，应该看到警告：
```
warning CAT1002: Handler 'BadHandler' contains blocking call 'Result'. Use async/await instead
```

## 📊 性能

分析器必须高效：

- ✅ 使用 `EnableConcurrentExecution()`
- ✅ 避免重复计算
- ✅ 使用符号比较而非字符串
- ✅ 缓存昂贵的查找

```csharp
// ❌ 慢
private static bool IsHandler(ITypeSymbol type)
{
    return type.AllInterfaces.Any(i => i.ToDisplayString() == "Catga.IRequestHandler");
}

// ✅ 快
private static bool IsHandler(ITypeSymbol type)
{
    return type.AllInterfaces.Any(i => i.Name == "IRequestHandler");
}
```

## 🐛 调试

在 Visual Studio 中调试分析器：

1. 设置 `Catga.SourceGenerator` 为启动项目
2. 项目属性 → Debug → Start external program
3. 选择 `C:\Program Files\Microsoft Visual Studio\...\devenv.exe`
4. Command line arguments: `/rootsuffix Roslyn`
5. F5 启动调试

## 📚 资源

- [Roslyn Analyzers Tutorial](https://github.com/dotnet/roslyn/blob/main/docs/wiki/How-To-Write-a-C%23-Analyzer-and-Code-Fix.md)
- [Roslyn API Documentation](https://docs.microsoft.com/dotnet/api/microsoft.codeanalysis)
- [Analyzer with Code Fix Template](https://marketplace.visualstudio.com/items?itemName=VisualStudioProductTeam.NETCompilerPlatformSDK)

---

**编写分析器，让 Catga 更易用！** 🎯
