# 🛠️ Catga测试工具和实用程序指南

**欢迎使用Catga TDD测试工具集！**

本指南汇总了所有可用的工具、脚本和实用程序，帮助您高效地运行测试、分析结果和维护代码质量。

---

## 📚 目录

1. [测试运行工具](#测试运行工具)
2. [测试分析工具](#测试分析工具)
3. [修复指南](#修复指南)
4. [文档资源](#文档资源)
5. [常见任务](#常见任务)
6. [故障排除](#故障排除)

---

## 🧪 测试运行工具

### 1. 便捷运行脚本

#### Windows (`run-new-tests.ps1`)

**位置**: `tests/run-new-tests.ps1`

**用法**:
```powershell
# 运行所有新增测试
.\tests\run-new-tests.ps1

# 运行特定测试
.\tests\run-new-tests.ps1 CircuitBreaker

# 收集覆盖率
.\tests\run-new-tests.ps1 -Coverage

# 使用过滤器
.\tests\run-new-tests.ps1 -Filter "FullyQualifiedName~ECommerce"

# 查看帮助
.\tests\run-new-tests.ps1 -Help
```

**功能**:
- ✅ 快速运行测试
- ✅ 支持测试过滤
- ✅ 覆盖率收集
- ✅ 清晰的输出格式
- ✅ 自动安装提示

#### Linux/macOS (`run-new-tests.sh`)

**位置**: `tests/run-new-tests.sh`

**用法**:
```bash
# 首次使用，添加执行权限
chmod +x tests/run-new-tests.sh

# 运行所有新增测试
./tests/run-new-tests.sh

# 运行特定测试
./tests/run-new-tests.sh CircuitBreaker

# 收集覆盖率
./tests/run-new-tests.sh --coverage

# 查看帮助
./tests/run-new-tests.sh --help
```

---

## 📊 测试分析工具

### 2. 高级分析脚本

#### Windows (`analyze-test-results.ps1`)

**位置**: `scripts/analyze-test-results.ps1`

**用法**:
```powershell
# 基本分析
.\scripts\analyze-test-results.ps1

# 完整分析（覆盖率 + 详细报告 + 自动打开）
.\scripts\analyze-test-results.ps1 -Coverage -Detailed -OpenReport

# 仅针对特定测试
.\scripts\analyze-test-results.ps1 -Filter "CircuitBreaker" -Detailed

# 包含集成测试
.\scripts\analyze-test-results.ps1 -SkipIntegration:$false

# 查看帮助
.\scripts\analyze-test-results.ps1 -Help
```

**输出**:
- 📊 彩色终端输出
- 📈 可视化进度条
- 📄 HTML测试报告
- 📊 代码覆盖率报告
- 📋 TRX测试结果文件

**特点**:
- 🎨 美观的HTML报告
- 📊 实时统计分析
- 📈 覆盖率可视化
- 🎯 质量评估
- 💡 改进建议

#### Linux/macOS (`analyze-test-results.sh`)

**位置**: `scripts/analyze-test-results.sh`

**用法**:
```bash
# 首次使用，添加执行权限
chmod +x scripts/analyze-test-results.sh

# 基本分析
./scripts/analyze-test-results.sh

# 完整分析
./scripts/analyze-test-results.sh -c -d -o

# 仅针对特定测试
./scripts/analyze-test-results.sh -f "CircuitBreaker" -d

# 包含集成测试
./scripts/analyze-test-results.sh -i

# 查看帮助
./scripts/analyze-test-results.sh --help
```

---

## 🔧 修复指南

### 3. 失败测试修复指南

**位置**: `tests/FIX_FAILING_TESTS_GUIDE.md`

**内容**:
- 🐛 11个失败测试的详细分析
- 💡 逐步修复方案
- 📝 代码示例
- 🎯 优先级排序
- ⏱️ 预计修复时间

**快速修复**:

```bash
# 1. 打开修复指南
code tests/FIX_FAILING_TESTS_GUIDE.md

# 2. 修复取消令牌问题（最简单，影响最大）
#    - 打开 src/Catga/CatgaMediator.cs
#    - 在方法开头添加: cancellationToken.ThrowIfCancellationRequested()
#    - 在循环中添加检查

# 3. 运行测试验证
dotnet test --filter "FullyQualifiedName~BatchProcessingEdgeCasesTests"

# 4. 修复其他问题
#    - 参考指南中的详细说明
```

**预期效果**:
- 修复5个取消令牌问题 → 通过率提升到97.4%
- 修复全部11个问题 → 通过率达到100% 🎉

---

## 📚 文档资源

### 核心文档

| 文档 | 描述 | 适合场景 |
|------|------|----------|
| [QUICK_START_TESTING.md](QUICK_START_TESTING.md) | 5分钟快速上手 | 新手入门 |
| [TEST_COVERAGE_SUMMARY.md](Catga.Tests/TEST_COVERAGE_SUMMARY.md) | 测试覆盖详情 | 了解覆盖率 |
| [TEST_EXECUTION_REPORT.md](TEST_EXECUTION_REPORT.md) | 实际执行结果 | 查看测试结果 |
| [TESTS_INDEX.md](Catga.Tests/TESTS_INDEX.md) | 测试快速索引 | 查找特定测试 |
| [TDD_IMPLEMENTATION_REPORT.md](Catga.Tests/TDD_IMPLEMENTATION_REPORT.md) | 完整实施报告 | 深入了解TDD |
| [FIX_FAILING_TESTS_GUIDE.md](FIX_FAILING_TESTS_GUIDE.md) | 修复失败测试 | 解决测试问题 |
| [TEST_METRICS_DASHBOARD.md](TEST_METRICS_DASHBOARD.md) | 测试指标仪表板 | 监控测试趋势 |
| [TOOLS_AND_UTILITIES.md](TOOLS_AND_UTILITIES.md) | 工具使用指南 | 本文档 |

### 项目文档

| 文档 | 描述 |
|------|------|
| [PROJECT_COMPLETION_REPORT.md](../PROJECT_COMPLETION_REPORT.md) | 项目完成报告 |
| [FINAL_PROJECT_SUMMARY.md](../FINAL_PROJECT_SUMMARY.md) | 最终项目总结 |
| [COMMIT_NOW.md](../COMMIT_NOW.md) | Git提交指南 |

---

## 🎯 常见任务

### 任务1: 运行所有测试

```bash
# 最简单的方式
dotnet test tests/Catga.Tests/Catga.Tests.csproj

# 或使用便捷脚本
.\tests\run-new-tests.ps1        # Windows
./tests/run-new-tests.sh         # Linux/macOS
```

### 任务2: 运行特定测试套件

```bash
# CircuitBreaker测试
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests"

# 多个测试套件
dotnet test --filter "FullyQualifiedName~CircuitBreaker|FullyQualifiedName~Concurrency"

# 使用脚本
.\tests\run-new-tests.ps1 CircuitBreaker
```

### 任务3: 生成覆盖率报告

```bash
# 方式1: 基本覆盖率
dotnet test /p:CollectCoverage=true

# 方式2: 使用脚本（推荐）
.\scripts\analyze-test-results.ps1 -Coverage -OpenReport

# 方式3: 生成HTML报告
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
reportgenerator -reports:"coverage_report/opencover.xml" -targetdir:"coverage_report/html"
```

### 任务4: 查看测试详情

```bash
# 详细输出
dotnet test --logger "console;verbosity=detailed"

# 生成TRX文件
dotnet test --logger "trx;LogFileName=test-results.trx"

# 使用分析工具（推荐）
.\scripts\analyze-test-results.ps1 -Detailed -OpenReport
```

### 任务5: 仅运行新增测试

```bash
# Windows
.\tests\run-new-tests.ps1

# Linux/macOS
./tests/run-new-tests.sh

# 或使用过滤器
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests|FullyQualifiedName~ConcurrencyLimiterTests|FullyQualifiedName~StreamProcessingTests|FullyQualifiedName~CorrelationTrackingTests|FullyQualifiedName~BatchProcessingEdgeCasesTests|FullyQualifiedName~EventHandlerFailureTests|FullyQualifiedName~HandlerCachePerformanceTests|FullyQualifiedName~ECommerceOrderFlowTests"
```

### 任务6: 排除集成测试

```bash
# 使用过滤器
dotnet test --filter "FullyQualifiedName!~Integration"

# 使用脚本（默认行为）
.\scripts\analyze-test-results.ps1
```

### 任务7: 持续监控测试

```bash
# 监视文件变化并自动运行测试
dotnet watch test tests/Catga.Tests/Catga.Tests.csproj

# 或创建自定义脚本
while ($true) {
    dotnet test --no-build
    Start-Sleep -Seconds 10
}
```

---

## 🐛 故障排除

### 问题1: 脚本无法执行（Windows）

**错误**: `无法加载文件，因为在此系统上禁止运行脚本`

**解决方案**:
```powershell
# 临时允许（推荐）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

# 或永久允许（需要管理员）
Set-ExecutionPolicy RemoteSigned
```

### 问题2: 脚本无法执行（Linux/macOS）

**错误**: `Permission denied`

**解决方案**:
```bash
# 添加执行权限
chmod +x tests/run-new-tests.sh
chmod +x scripts/analyze-test-results.sh
```

### 问题3: 找不到dotnet命令

**错误**: `dotnet: command not found`

**解决方案**:
1. 确认已安装.NET SDK
2. 检查PATH环境变量
3. 重启终端/IDE

```bash
# 检查安装
dotnet --version

# Windows添加到PATH
$env:PATH += ";C:\Program Files\dotnet"

# Linux/macOS添加到PATH
export PATH="$PATH:$HOME/.dotnet"
```

### 问题4: 覆盖率收集失败

**错误**: `Coverlet instrumentation error`

**解决方案**:
```bash
# 确认已安装coverlet
dotnet add package coverlet.collector

# 或使用全局工具
dotnet tool install -g coverlet.console
```

### 问题5: ReportGenerator未找到

**错误**: `reportgenerator: command not found`

**解决方案**:
```bash
# 安装全局工具
dotnet tool install -g dotnet-reportgenerator-globaltool

# 确认安装
reportgenerator --version
```

### 问题6: 测试运行缓慢

**原因**: 包含了集成测试或外部依赖

**解决方案**:
```bash
# 跳过集成测试
dotnet test --filter "FullyQualifiedName!~Integration"

# 使用并行测试
dotnet test --parallel

# 减少详细度
dotnet test --logger "console;verbosity=minimal"
```

### 问题7: 内存不足

**错误**: `OutOfMemoryException`

**解决方案**:
```bash
# 增加堆内存
$env:DOTNET_CLI_TELEMETRY_OPTOUT=1
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# 或分批运行测试
dotnet test --filter "FullyQualifiedName~CircuitBreaker"
dotnet test --filter "FullyQualifiedName~Concurrency"
```

---

## 💡 最佳实践

### 1. 测试运行习惯

✅ **每次提交前运行测试**
```bash
git add .
.\scripts\analyze-test-results.ps1 -Detailed
git commit -m "feat: xxx"
```

✅ **定期生成覆盖率报告**
```bash
# 每周运行一次
.\scripts\analyze-test-results.ps1 -Coverage -Detailed -OpenReport
```

✅ **使用过滤器快速迭代**
```bash
# 只运行正在开发的功能测试
dotnet test --filter "FullyQualifiedName~YourNewFeature"
```

### 2. 持续集成

✅ **在CI/CD中使用**
```yaml
# GitHub Actions
- name: Run Tests
  run: dotnet test --no-build --verbosity normal --logger trx

- name: Publish Test Results
  if: always()
  uses: EnricoMi/publish-unit-test-result-action@v2
  with:
    files: '**/*.trx'
```

### 3. 团队协作

✅ **共享测试报告**
```bash
# 生成报告
.\scripts\analyze-test-results.ps1 -Detailed

# 共享HTML文件
# test-reports/test-report-YYYY-MM-DD_HH-mm-ss.html
```

✅ **标准化测试命令**
```bash
# 在README中添加
npm run test:unit     # 运行单元测试
npm run test:coverage # 运行覆盖率测试
npm run test:all      # 运行所有测试
```

---

## 🚀 高级用法

### 性能分析

```bash
# 使用BenchmarkDotNet
dotnet run -c Release --project benchmarks/Catga.Benchmarks

# 分析慢测试
dotnet test --logger "console;verbosity=detailed" | grep "ms"
```

### 调试测试

```bash
# VS Code
# 在测试上右键 -> Debug Test

# Visual Studio
# 测试资源管理器 -> 右键 -> 调试

# 命令行
dotnet test --filter "TestName~SpecificTest" --logger "console;verbosity=detailed"
```

### 自定义报告

```bash
# 生成自定义JSON报告
dotnet test --logger "json;LogFileName=custom-report.json"

# 生成JUnit格式（用于Jenkins）
dotnet test --logger "junit;LogFileName=junit-results.xml"
```

---

## 📈 监控和维护

### 定期任务

| 频率 | 任务 | 命令 |
|------|------|------|
| 每次提交 | 运行单元测试 | `dotnet test --filter "!~Integration"` |
| 每天 | 运行所有测试 | `.\scripts\analyze-test-results.ps1` |
| 每周 | 生成覆盖率报告 | `.\scripts\analyze-test-results.ps1 -Coverage -Detailed -OpenReport` |
| 每月 | 审查失败测试 | 参考 `FIX_FAILING_TESTS_GUIDE.md` |
| 每季度 | 更新测试文档 | 审查和更新所有MD文件 |

### 质量目标

| 指标 | 目标 | 当前 | 趋势 |
|------|------|------|------|
| 测试通过率 | > 95% | 94.3% | ⬆️ |
| 代码覆盖率 | > 90% | ~90% | ✅ |
| 平均执行时间 | < 60s | 57s | ✅ |
| 失败测试数 | < 5 | 11 | ⚠️ |

---

## 🎓 学习资源

### 内部资源

- [TDD实施报告](Catga.Tests/TDD_IMPLEMENTATION_REPORT.md) - 学习TDD方法论
- [测试覆盖总结](Catga.Tests/TEST_COVERAGE_SUMMARY.md) - 了解测试架构
- [修复指南](FIX_FAILING_TESTS_GUIDE.md) - 掌握调试技巧

### 外部资源

- [xUnit文档](https://xunit.net/) - xUnit测试框架
- [FluentAssertions](https://fluentassertions.com/) - 断言库
- [Coverlet](https://github.com/coverlet-coverage/coverlet) - 覆盖率工具
- [ReportGenerator](https://github.com/danielpalme/ReportGenerator) - 报告生成

---

## 🤝 获取帮助

### 文档不清楚？

1. 查看 [快速开始指南](QUICK_START_TESTING.md)
2. 搜索 [测试索引](Catga.Tests/TESTS_INDEX.md)
3. 参考 [故障排除](#故障排除)

### 遇到问题？

1. 查看 [测试执行报告](TEST_EXECUTION_REPORT.md) - 了解已知问题
2. 查看 [修复指南](FIX_FAILING_TESTS_GUIDE.md) - 常见问题解决方案
3. 提交Issue到GitHub

### 想要改进？

1. Fork项目
2. 创建功能分支
3. 提交Pull Request

---

## 📞 联系方式

- 📧 Email: your-team@example.com
- 💬 Slack: #catga-testing
- 📁 GitHub: [Catga Repository](https://github.com/your-org/Catga)
- 📚 文档: [Catga Docs](https://cricle.github.io/Catga/)

---

<div align="center">

## 🎉 开始使用吧！

选择您的平台，运行第一个测试：

```powershell
# Windows
.\tests\run-new-tests.ps1
```

```bash
# Linux/macOS
./tests/run-new-tests.sh
```

**祝您测试愉快！** 🚀

---

**文档版本**: v1.0
**最后更新**: 2025-10-26

</div>

