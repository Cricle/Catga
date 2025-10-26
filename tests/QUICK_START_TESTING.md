# 🚀 测试快速开始指南

## 5分钟快速上手

### 方式1：使用便捷脚本（推荐）

#### Windows (PowerShell)
```powershell
# 运行所有新增测试
.\tests\run-new-tests.ps1

# 运行特定测试
.\tests\run-new-tests.ps1 -CircuitBreaker
.\tests\run-new-tests.ps1 -Concurrency
.\tests\run-new-tests.ps1 -ECommerce

# 收集覆盖率
.\tests\run-new-tests.ps1 -Coverage

# 详细输出
.\tests\run-new-tests.ps1 -Verbose -Coverage

# 查看帮助
.\tests\run-new-tests.ps1 -Help
```

#### Linux/macOS (Bash)
```bash
# 添加执行权限（首次）
chmod +x tests/run-new-tests.sh

# 运行所有新增测试
./tests/run-new-tests.sh

# 运行特定测试
./tests/run-new-tests.sh --circuit-breaker
./tests/run-new-tests.sh --concurrency
./tests/run-new-tests.sh --ecommerce

# 收集覆盖率
./tests/run-new-tests.sh --coverage

# 详细输出
./tests/run-new-tests.sh --verbose --coverage

# 查看帮助
./tests/run-new-tests.sh --help
```

---

### 方式2：使用dotnet CLI

#### 运行所有测试
```bash
dotnet test tests/Catga.Tests/Catga.Tests.csproj
```

#### 运行特定测试文件
```bash
# 熔断器测试
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests"

# 并发限制器测试
dotnet test --filter "FullyQualifiedName~ConcurrencyLimiterTests"

# 流式处理测试
dotnet test --filter "FullyQualifiedName~StreamProcessingTests"

# 消息追踪测试
dotnet test --filter "FullyQualifiedName~CorrelationTrackingTests"

# 批处理测试
dotnet test --filter "FullyQualifiedName~BatchProcessingEdgeCasesTests"

# 事件失败测试
dotnet test --filter "FullyQualifiedName~EventHandlerFailureTests"

# Handler缓存测试
dotnet test --filter "FullyQualifiedName~HandlerCachePerformanceTests"

# 电商订单测试
dotnet test --filter "FullyQualifiedName~ECommerceOrderFlowTests"
```

#### 运行单个测试
```bash
# 语法
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# 示例
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests.ExecuteAsync_InClosedState_ShouldExecuteSuccessfully"
```

#### 详细输出
```bash
dotnet test --logger "console;verbosity=detailed"
```

#### 收集覆盖率
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

---

## 📊 查看覆盖率报告

### 1. 安装reportgenerator（首次）
```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

### 2. 生成HTML报告
```bash
# 运行测试并收集覆盖率
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# 生成HTML报告
reportgenerator -reports:tests/Catga.Tests/coverage.cobertura.xml -targetdir:coveragereport

# 打开报告
# Windows
start coveragereport/index.html

# Linux
xdg-open coveragereport/index.html

# macOS
open coveragereport/index.html
```

---

## 🎯 按场景运行测试

### 并发和性能测试
```bash
# 熔断器、并发限制器、Handler缓存
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests|ConcurrencyLimiterTests|HandlerCachePerformanceTests"
```

### 消息处理测试
```bash
# 流式处理、消息追踪、批处理
dotnet test --filter "FullyQualifiedName~StreamProcessingTests|CorrelationTrackingTests|BatchProcessingEdgeCasesTests"
```

### 错误处理测试
```bash
# 事件失败处理
dotnet test --filter "FullyQualifiedName~EventHandlerFailureTests"
```

### 业务场景测试
```bash
# 电商订单流程
dotnet test --filter "FullyQualifiedName~ECommerceOrderFlowTests"
```

---

## 🔧 CI/CD 集成

### GitHub Actions 示例
```yaml
name: Run TDD Tests

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Run TDD Tests
      run: dotnet test tests/Catga.Tests/Catga.Tests.csproj --no-build --verbosity normal

    - name: Collect Coverage
      run: dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

    - name: Upload Coverage
      uses: codecov/codecov-action@v3
      with:
        files: tests/Catga.Tests/coverage.cobertura.xml
```

### Azure DevOps 示例
```yaml
trigger:
  - main
  - develop

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '9.0.x'

- task: DotNetCoreCLI@2
  displayName: 'Restore'
  inputs:
    command: 'restore'

- task: DotNetCoreCLI@2
  displayName: 'Build'
  inputs:
    command: 'build'
    arguments: '--no-restore'

- task: DotNetCoreCLI@2
  displayName: 'Run TDD Tests'
  inputs:
    command: 'test'
    projects: 'tests/Catga.Tests/Catga.Tests.csproj'
    arguments: '--no-build --logger trx --collect:"XPlat Code Coverage"'

- task: PublishTestResults@2
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/*.trx'
```

---

## 📝 常见问题

### Q: 测试运行很慢怎么办？
**A**: 使用并行测试
```bash
dotnet test --parallel
```

### Q: 如何跳过慢速测试？
**A**: 为慢速测试添加Trait并过滤
```bash
dotnet test --filter "Category!=Slow"
```

### Q: 如何在Docker中运行测试？
**A**: 使用Docker命令
```bash
docker run --rm -v $(pwd):/app -w /app mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet test tests/Catga.Tests/Catga.Tests.csproj
```

### Q: 测试失败如何调试？
**A**: 使用详细输出
```bash
dotnet test --logger "console;verbosity=detailed"
```

---

## 📚 文档导航

- 📖 [测试覆盖总结](TEST_COVERAGE_SUMMARY.md) - 详细的测试覆盖情况
- 📘 [新增测试说明](NEW_TESTS_README.md) - 完整的测试说明
- 📙 [TDD实施报告](TDD_IMPLEMENTATION_REPORT.md) - 实施详情和质量指标
- 📗 [测试快速索引](TESTS_INDEX.md) - 所有测试用例索引

---

## 🎓 测试最佳实践

### 运行测试前
1. ✅ 确保代码已编译
2. ✅ 清理之前的测试输出
3. ✅ 更新依赖包

### 运行测试时
1. ✅ 先运行单元测试
2. ✅ 然后运行集成测试
3. ✅ 最后运行场景测试

### 运行测试后
1. ✅ 检查测试结果
2. ✅ 查看覆盖率报告
3. ✅ 分析失败原因

---

## 🚀 性能提示

### 加速测试运行
```bash
# 并行运行
dotnet test --parallel

# 不重新构建
dotnet test --no-build

# 不恢复依赖
dotnet test --no-restore --no-build
```

### 只运行失败的测试
```bash
# 首次运行
dotnet test --logger "trx"

# 只运行失败的测试（需要插件支持）
# 或者根据trx结果手动过滤
```

---

## ✨ 快速命令参考

```bash
# 基础命令
dotnet test                                    # 运行所有测试
dotnet test --filter "Name~Test"              # 过滤测试
dotnet test --logger "console;verbosity=normal" # 控制输出

# 覆盖率
dotnet test /p:CollectCoverage=true            # 收集覆盖率

# 输出
dotnet test --logger "trx"                     # TRX格式
dotnet test --logger "html"                    # HTML格式

# 性能
dotnet test --parallel                         # 并行运行
dotnet test --no-build                         # 不重新构建
```

---

**祝测试愉快！🎉**

如有问题，请查看详细文档或提交Issue。

