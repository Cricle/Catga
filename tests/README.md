# Catga Tests

## 快速开始

### 运行所有测试
```powershell
# Catga.Tests (单元测试 + 属性测试 + 集成测试)
dotnet test tests/Catga.Tests/Catga.Tests.csproj

# E2E Tests
dotnet test tests/Catga.E2E.Tests/Catga.E2E.Tests.csproj
```

### 运行特定类别
```powershell
# 只运行属性测试
dotnet test tests/Catga.Tests/Catga.Tests.csproj --filter "Category=Property"

# 只运行集成测试
dotnet test tests/Catga.Tests/Catga.Tests.csproj --filter "Category=Integration"
```

### 运行当前主闭环快回归
```bash
# 核心闭环：FlowDsl / CQRS / Mediator / Pipeline / Outbox-Inbox / Redis/NATS FlowStore
scripts/test-fast-regression.sh core

# 支持后端闭环：Redis + NATS transport / persistence / lock / batching / failover
scripts/test-fast-regression.sh backends

# 合并闭环：当前主功能 + Redis/NATS 支持后端
scripts/test-fast-regression.sh all
```

当前已验证的规模：
- `core`: 128 tests
- `backends`: 245 tests
- `all`: 373 tests

说明：
- 以上快回归固定使用 `net8.0` 和单进程 test 参数，优先减少本地抖动与编译开销。
- 脚本默认会在本机缺少 Redis(`127.0.0.1:6379`) / NATS(`127.0.0.1:4222`) 时自动尝试用 Docker 拉起 `catga-redis-test` 和 `catga-nats-test`。
- 如需禁用自动拉起，可设置 `CATGA_AUTO_START_BACKENDS=0`。

## 性能优化

测试已经过性能优化，使用共享容器基础设施：
- ✅ 容器只启动一次，所有测试共享
- ✅ PropertyTests: ~30秒
- ✅ E2E Tests: ~45秒
- ✅ 容器启动次数: 2-3次（优化前50+次）

详见: [OPTIMIZATION-DONE.md](OPTIMIZATION-DONE.md)

## 前置要求

- Docker Desktop 必须运行
- .NET 8.0 或 9.0 SDK

## 测试结构

```
tests/
├── Catga.Tests/              # 单元测试 + 属性测试 + 集成测试
│   ├── Core/                 # 核心功能测试
│   ├── PropertyTests/        # 基于属性的测试（FsCheck）
│   ├── Integration/          # 集成测试（Redis/NATS）
│   └── ...
├── Catga.E2E.Tests/          # 端到端测试（OrderSystem示例）
└── Catga.AotValidation/      # AOT编译验证
```

## 故障排查

### 容器启动失败
```powershell
# 检查Docker
docker info

# 拉取镜像
docker pull redis:7-alpine
docker pull nats:2.10-alpine
```

### 测试失败
```powershell
# 查看详细日志
dotnet test --logger "console;verbosity=detailed"

# 清理容器
docker ps -a | Select-String "catga" | ForEach-Object { docker rm -f $_.ToString().Split()[0] }
```

## CI/CD

测试在CI环境中自动运行，使用相同的共享容器优化。
