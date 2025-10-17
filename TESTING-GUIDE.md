# Catga OrderSystem 测试指南

完整的测试脚本集合，用于验证 OrderSystem 示例的所有功能。

---

## 📋 测试脚本概览

| 脚本 | 用途 | 适用场景 |
|------|------|---------|
| `quick-test.ps1` | 快速测试 5 个核心功能 | 服务已运行，快速验证 |
| `test-ordersystem-full.ps1` | 全面测试所有 API 和页面 | 完整功能验证 |
| `run-and-test.ps1` | 一键启动服务并测试 | 从零开始自动化测试 |

---

## 🚀 快速开始

### 方式 1: 快速测试（推荐用于已运行的服务）

```powershell
# 1. 启动服务（如果未启动）
cd examples/OrderSystem.AppHost
dotnet run

# 2. 在另一个终端运行快速测试
.\quick-test.ps1
```

**输出示例**:
```
⚡ Catga OrderSystem 快速测试

✅ 服务运行中

🧪 测试核心功能...

1️⃣  创建订单... ✅
2️⃣  查询订单... ✅
3️⃣  Debugger API... ✅ (3 个流)
4️⃣  页面访问... ✅
5️⃣  Debugger UI... ✅

─────────────────────────────────
🎉 全部通过! (5/5, 100%)
```

---

### 方式 2: 全面测试（推荐用于功能验证）

```powershell
# 1. 启动服务（如果未启动）
cd examples/OrderSystem.AppHost
dotnet run

# 2. 在另一个终端运行全面测试
.\test-ordersystem-full.ps1
```

**测试覆盖**:
- ✅ 健康检查 (3 个端点)
- ✅ API 端点 (订单创建/查询/取消，成功和失败场景)
- ✅ Debugger API (消息流/统计/事件)
- ✅ 页面访问 (主页/UI/Debugger/时间旅行/断点/性能)
- ✅ 静态资源 (JS/CSS 库文件)
- ✅ Swagger (API 文档)
- ✅ SignalR (实时通信)

**输出示例**:
```
🧪 Catga OrderSystem 全面测试
================================

📊 1. 健康检查测试
─────────────────────────────────────────
  ✅ Health Check (/health)
  ✅ Liveness Check (/health/live)
  ✅ Readiness Check (/health/ready)

🔌 2. API 端点测试
─────────────────────────────────────────
  ✅ 创建订单 - 成功场景 (订单ID: ORD-12345)
  ✅ 创建订单 - 失败场景 (库存不足)
  ✅ 查询订单 (ID: ORD-12345)
  ✅ 取消订单 (ID: ORD-12345)

... (更多测试结果)

═══════════════════════════════════════════════════
📊 测试结果统计
═══════════════════════════════════════════════════

总测试数: 28
通过: 28 ✅
失败: 0 ❌
通过率: 100%

🎉 所有测试通过！系统运行正常！

可访问的 URL:
  • OrderSystem UI: http://localhost:5000
  • Catga Debugger: http://localhost:5000/debugger/index.html
  • Swagger API: http://localhost:5000/swagger
  • Aspire Dashboard: http://localhost:15888
```

---

### 方式 3: 一键启动和测试（推荐用于自动化）

```powershell
# 自动构建、启动服务、运行测试、清理
.\run-and-test.ps1
```

**流程**:
1. ✅ 检查现有进程
2. ✅ 构建项目
3. ✅ 后台启动服务
4. ✅ 等待服务就绪
5. ✅ 运行全面测试
6. ✅ 询问是否保持服务运行

---

## 📊 测试覆盖详情

### 1. 健康检查 (3 个端点)

| 端点 | 说明 |
|------|------|
| `/health` | 综合健康检查 |
| `/health/live` | 存活探测 (Liveness) |
| `/health/ready` | 就绪探测 (Readiness) |

### 2. API 端点 (订单管理)

| API | 方法 | 说明 |
|-----|------|------|
| `/api/orders` | POST | 创建订单（成功场景） |
| `/api/orders` | POST | 创建订单（失败场景 - 库存不足） |
| `/api/orders/{id}` | GET | 查询订单 |
| `/api/orders/{id}/cancel` | POST | 取消订单 |

### 3. Debugger API (调试监控)

| API | 方法 | 说明 |
|-----|------|------|
| `/debug-api/flows` | GET | 获取所有消息流 |
| `/debug-api/stats` | GET | 获取统计信息 |
| `/debug-api/flows/{id}` | GET | 获取消息流详情 |
| `/debug-api/flows/{id}/events` | GET | 获取流事件 |

### 4. 页面访问 (UI)

| 页面 | 说明 |
|------|------|
| `/` | 主页 |
| `/index.html` | OrderSystem UI |
| `/debugger/index.html` | Debugger 主页 |
| `/debugger/replay-player.html` | 时间旅行调试器 |
| `/debugger/breakpoints.html` | 断点调试器 |
| `/debugger/profiling.html` | 性能分析器 |

### 5. 静态资源 (JS/CSS)

| 资源 | 说明 |
|------|------|
| `/lib/alpine.min.js` | Alpine.js (OrderSystem) |
| `/lib/tailwind.js` | Tailwind CSS (OrderSystem) |
| `/lib/signalr.min.js` | SignalR (Debugger) |

### 6. Swagger (API 文档)

| 页面 | 说明 |
|------|------|
| `/swagger/index.html` | Swagger UI |
| `/swagger/v1/swagger.json` | OpenAPI 规范 |

### 7. SignalR (实时通信)

| 端点 | 说明 |
|------|------|
| `/debugger-hub/negotiate` | SignalR 连接协商 |

---

## 🔧 故障排除

### 问题 1: 服务启动超时

**症状**:
```
❌ 服务启动超时
```

**解决方案**:
```powershell
# 1. 检查端口占用
netstat -ano | findstr :5000
netstat -ano | findstr :15888

# 2. 手动启动查看日志
cd examples/OrderSystem.AppHost
dotnet run

# 3. 检查 Aspire Dashboard 是否启动
# 访问 http://localhost:15888
```

---

### 问题 2: 测试失败

**症状**:
```
❌ 创建订单 - 成功场景 (错误: ...)
```

**解决方案**:
```powershell
# 1. 查看详细错误信息
# 测试脚本会显示具体错误

# 2. 检查服务日志
# 在运行 dotnet run 的终端查看日志

# 3. 检查健康状态
curl http://localhost:5000/health
```

---

### 问题 3: 页面无法访问

**症状**:
```
❌ OrderSystem UI (/index.html) (错误: ...)
```

**解决方案**:
```powershell
# 1. 检查静态文件是否存在
ls examples/OrderSystem.Api/wwwroot/index.html

# 2. 检查静态文件中间件配置
# Program.cs 应该有: app.UseStaticFiles()

# 3. 手动访问页面
Start-Process "http://localhost:5000/index.html"
```

---

### 问题 4: Debugger UI 404

**症状**:
```
❌ Debugger 主页 (/debugger/index.html) (状态码: 404)
```

**解决方案**:
```powershell
# 1. 检查 Debugger 静态文件
ls src/Catga.Debugger.AspNetCore/wwwroot/debugger/index.html

# 2. 检查 Debugger 配置
# Program.cs 应该有: app.MapCatgaDebugger("/debug")

# 3. 正确的访问路径
# http://localhost:5000/debugger/index.html
```

---

## 📈 性能基准

基于全面测试的性能数据：

| 指标 | 值 |
|------|-----|
| **总测试数** | 28 |
| **测试执行时间** | ~10-15 秒 |
| **服务启动时间** | ~5-10 秒 |
| **API 响应时间** | < 100ms (本地) |
| **页面加载时间** | < 500ms (本地) |

---

## 🎯 CI/CD 集成

### GitHub Actions 示例

```yaml
name: Test OrderSystem

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'

      - name: Build
        run: dotnet build

      - name: Run Tests
        run: .\run-and-test.ps1
        shell: pwsh
```

---

## 📚 相关文档

- [OrderSystem 示例文档](examples/README-ORDERSYSTEM.md)
- [Catga Debugger 指南](docs/guides/debugger-aspire-integration.md)
- [Aspire 集成指南](ASPIRE-INTEGRATION-PLAN.md)

---

## 💡 最佳实践

### 1. 定期运行测试
```powershell
# 每次修改后运行快速测试
.\quick-test.ps1

# 每次提交前运行全面测试
.\test-ordersystem-full.ps1
```

### 2. 监控测试通过率
- 目标: 保持 100% 通过率
- 如果通过率 < 90%，立即调查
- 记录失败的测试详情

### 3. 使用测试脚本进行开发
```powershell
# 开发时保持服务运行
cd examples/OrderSystem.AppHost
dotnet watch run

# 在另一个终端不断运行快速测试
while ($true) {
    .\quick-test.ps1
    Start-Sleep -Seconds 10
}
```

---

## 🎉 总结

这套测试脚本提供了：

- ✅ **快速验证**: `quick-test.ps1` - 5 个核心功能，< 5 秒
- ✅ **全面测试**: `test-ordersystem-full.ps1` - 28 个测试，< 15 秒
- ✅ **自动化**: `run-and-test.ps1` - 一键启动和测试
- ✅ **详细报告**: 通过率、失败详情、建议
- ✅ **易于集成**: CI/CD、开发工作流

**确保 Catga OrderSystem 始终处于最佳状态！** 🚀

