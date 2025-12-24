# OrderSystem API 自动化测试

自动验证所有 OrderSystem API 端点的测试脚本。

## 功能特性

- ✅ 自动测试所有 API 端点
- ✅ 完整的订单生命周期测试（创建 → 支付 → 发货 → 取消）
- ✅ 健康检查和系统信息验证
- ✅ 统计数据验证
- ✅ 错误处理测试
- ✅ 彩色输出和详细报告
- ✅ 跨平台支持（Windows PowerShell / Linux Bash）

## 测试覆盖的端点

### 系统端点
- `GET /` - 系统信息
- `GET /health` - 健康检查（全部）
- `GET /health/ready` - 就绪探针
- `GET /health/live` - 存活探针

### 订单管理
- `POST /orders` - 创建订单
- `GET /orders` - 获取订单列表
- `GET /orders/{id}` - 获取订单详情
- `POST /orders/{id}/pay` - 支付订单
- `POST /orders/{id}/ship` - 发货订单
- `POST /orders/{id}/cancel` - 取消订单
- `GET /orders/{id}/history` - 获取订单事件历史

### 统计信息
- `GET /stats` - 获取统计数据

## 使用方法

### Windows (PowerShell)

```powershell
# 基本使用（默认 http://localhost:5000）
.\test-api.ps1

# 指定自定义 URL
.\test-api.ps1 -BaseUrl "http://localhost:8080"

# 显示详细输出
.\test-api.ps1 -Verbose

# 组合使用
.\test-api.ps1 -BaseUrl "http://localhost:8080" -Verbose
```

### Linux / macOS (Bash)

```bash
# 添加执行权限（首次运行）
chmod +x test-api.sh

# 基本使用（默认 http://localhost:5000）
./test-api.sh

# 指定自定义 URL
./test-api.sh http://localhost:8080

# 显示详细输出
VERBOSE=true ./test-api.sh

# 组合使用
VERBOSE=true ./test-api.sh http://localhost:8080
```

## 前置条件

### 启动 OrderSystem 服务

在运行测试之前，确保 OrderSystem 服务正在运行：

```bash
cd examples/OrderSystem
dotnet run
```

服务默认运行在 `http://localhost:5000`

### 依赖工具

**Windows:**
- PowerShell 5.1+ 或 PowerShell Core 7+

**Linux/macOS:**
- Bash 4.0+
- curl
- grep

## 测试流程

脚本会按以下顺序执行测试：

1. **服务可用性检查** - 验证服务是否运行
2. **系统信息** - 获取节点、模式、传输、持久化信息
3. **健康检查** - 验证所有健康检查端点
4. **统计信息** - 获取初始统计数据
5. **订单列表** - 获取现有订单
6. **创建订单** - 创建新的测试订单
7. **订单详情** - 查询新创建的订单
8. **支付订单** - 标记订单为已支付
9. **发货订单** - 标记订单为已发货
10. **订单历史** - 查看事件历史
11. **取消订单** - 创建并取消另一个订单
12. **验证更新** - 确认订单列表和统计数据已更新
13. **错误处理** - 测试 404 等错误场景

## 输出示例

```
╔══════════════════════════════════════════════════════════════╗
║          Catga OrderSystem - API 自动化测试                  ║
╠══════════════════════════════════════════════════════════════╣
║  基础 URL: http://localhost:5000                             ║
║  时间: 2024-12-24 10:30:45                                   ║
╚══════════════════════════════════════════════════════════════╝

ℹ 检查服务可用性...
✓ 服务正在运行

开始执行 API 测试...
================================================================

[1] 测试: 获取系统信息
   描述: 验证系统基本信息
   方法: GET http://localhost:5000/
✓ 通过

[2] 测试: 健康检查 (全部)
   描述: 验证系统整体健康状态
   方法: GET http://localhost:5000/health
✓ 通过

...

╔══════════════════════════════════════════════════════════════╗
║                      测试报告                                ║
╠══════════════════════════════════════════════════════════════╣
║  总测试数: 14                                                ║
║  通过: 14                                                    ║
║  失败: 0                                                     ║
║  通过率: 100%                                                ║
╚══════════════════════════════════════════════════════════════╝

✓ 所有测试通过！系统运行正常。
```

## 退出码

- `0` - 所有测试通过
- `1` - 有测试失败或服务不可用

## CI/CD 集成

可以在 CI/CD 管道中使用这些脚本：

### GitHub Actions 示例

```yaml
- name: Run API Tests
  run: |
    cd examples/OrderSystem
    dotnet run &
    sleep 5
    pwsh ./test-api.ps1
```

### GitLab CI 示例

```yaml
test:api:
  script:
    - cd examples/OrderSystem
    - dotnet run &
    - sleep 5
    - ./test-api.sh
```

## 故障排除

### 服务不可用

如果看到 "服务不可用" 错误：

1. 确认 OrderSystem 正在运行
2. 检查端口是否正确（默认 5000）
3. 验证防火墙设置

### 测试失败

如果某些测试失败：

1. 使用 `-Verbose` 参数查看详细输出
2. 检查服务日志
3. 手动测试失败的端点
4. 确认数据库/持久化层正常

### 权限问题（Linux/macOS）

```bash
chmod +x test-api.sh
```

## 自定义测试

可以修改脚本添加自定义测试：

```powershell
# PowerShell 示例
Test-Endpoint `
    -Name "自定义测试" `
    -Method "GET" `
    -Endpoint "/custom-endpoint" `
    -Description "测试自定义功能" `
    -Validator {
        param($response)
        return ($response.customField -eq "expectedValue")
    }
```

## 贡献

欢迎提交 Issue 和 Pull Request 来改进测试脚本！

## 许可证

与 Catga 项目相同的许可证。
