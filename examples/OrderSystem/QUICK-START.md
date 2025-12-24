# OrderSystem 快速开始指南

## 🚀 5 分钟快速体验

### 1️⃣ 启动服务

```bash
cd examples/OrderSystem
dotnet run
```

服务将在 `http://localhost:5000` 启动

### 2️⃣ 访问 Web UI

打开浏览器访问: **http://localhost:5000**

- 🎨 美观的现代化界面
- 🌓 支持深色/浅色模式切换
- 📱 完全响应式设计
- 🔄 实时自动刷新

### 3️⃣ 运行自动化测试

**Windows (PowerShell):**
```powershell
.\test-api.ps1
```

**Linux/Mac (Bash):**
```bash
chmod +x test-api.sh
./test-api.sh
```

## 📋 常用命令

### 启动不同配置

```bash
# 默认 (InMemory)
dotnet run

# Redis 后端
docker run -d -p 6379:6379 redis:alpine
dotnet run -- --transport redis --persistence redis

# NATS 后端
docker run -d -p 4222:4222 nats:alpine -js
dotnet run -- --transport nats --persistence nats

# 集群模式 (3 节点)
dotnet run -- --cluster --node-id node1 --port 5001 --transport redis
dotnet run -- --cluster --node-id node2 --port 5002 --transport redis
dotnet run -- --cluster --node-id node3 --port 5003 --transport redis
```

### API 测试命令

```bash
# 基本测试
.\test-api.ps1                              # Windows
./test-api.sh                               # Linux/Mac

# 自定义 URL
.\test-api.ps1 -BaseUrl "http://localhost:8080"
./test-api.sh http://localhost:8080

# 详细输出
.\test-api.ps1 -Verbose
VERBOSE=true ./test-api.sh
```

### 手动 API 测试

```bash
# 创建订单
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"test-001","items":[{"productId":"p1","name":"商品","quantity":1,"price":99.99}]}'

# 获取订单列表
curl http://localhost:5000/orders

# 获取统计信息
curl http://localhost:5000/stats

# 健康检查
curl http://localhost:5000/health
```

## 🎯 核心功能演示

### Web UI 功能

1. **创建订单**
   - 填写客户 ID、商品信息
   - 点击"创建订单"按钮
   - 查看成功提示和新订单

2. **管理订单**
   - 点击"支付"按钮完成支付
   - 点击"发货"按钮标记发货
   - 点击"取消"按钮取消订单
   - 点击"历史"查看事件历史

3. **查看统计**
   - 实时订单总数
   - 总收入统计
   - 按状态分类统计
   - 自动刷新（10秒）

4. **主题切换**
   - 点击右上角主题按钮
   - 在深色/浅色模式间切换
   - 偏好自动保存

### API 端点

| 端点 | 方法 | 说明 |
|------|------|------|
| `/` | GET | 系统信息 |
| `/health` | GET | 健康检查 |
| `/stats` | GET | 统计数据 |
| `/orders` | GET | 订单列表 |
| `/orders` | POST | 创建订单 |
| `/orders/{id}` | GET | 订单详情 |
| `/orders/{id}/pay` | POST | 支付订单 |
| `/orders/{id}/ship` | POST | 发货订单 |
| `/orders/{id}/cancel` | POST | 取消订单 |
| `/orders/{id}/history` | GET | 事件历史 |

## 🔧 配置选项

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--transport` | `inmemory` | 传输层: `inmemory`, `redis`, `nats` |
| `--persistence` | `inmemory` | 持久化: `inmemory`, `redis`, `nats` |
| `--redis` | `localhost:6379` | Redis 连接字符串 |
| `--nats` | `nats://localhost:4222` | NATS 服务器 URL |
| `--cluster` | `false` | 启用集群模式 |
| `--node-id` | `auto` | 节点标识符 |
| `--port` | `5000` | HTTP 端口 |

## 📊 测试覆盖

自动化测试脚本覆盖：

- ✅ 系统信息和健康检查
- ✅ 订单完整生命周期
  - 创建 → 支付 → 发货
  - 创建 → 取消
- ✅ 订单查询和列表
- ✅ 事件历史追踪
- ✅ 统计数据验证
- ✅ 错误处理（404 等）

## 🐛 故障排除

### 端口被占用
```bash
# 使用其他端口
dotnet run -- --port 5001
```

### Redis 连接失败
```bash
# 检查 Redis 是否运行
docker ps | grep redis

# 启动 Redis
docker run -d -p 6379:6379 redis:alpine
```

### NATS 连接失败
```bash
# 检查 NATS 是否运行
docker ps | grep nats

# 启动 NATS
docker run -d -p 4222:4222 nats:alpine -js
```

### 测试脚本权限错误 (Linux/Mac)
```bash
chmod +x test-api.sh
```

## 📚 更多资源

- [完整 README](./README.md) - 详细文档
- [API 测试文档](./TEST-API-README.md) - 测试脚本详解
- [Catga 文档](../../docs/README.md) - 框架文档
- [架构设计](../../docs/architecture/) - 架构说明

## 💡 提示

1. **开发环境**: 使用 InMemory 配置，快速启动
2. **生产环境**: 使用 Redis 或 NATS，获得更好性能
3. **集群部署**: 启用集群模式，实现负载均衡
4. **CI/CD**: 集成 test-api 脚本到自动化流程
5. **监控**: 使用 `/health` 和 `/stats` 端点监控系统

## 🎉 开始探索

现在你已经准备好探索 Catga OrderSystem 的所有功能了！

1. 启动服务
2. 打开 Web UI
3. 创建一些订单
4. 运行自动化测试
5. 尝试不同的配置

祝你使用愉快！🚀
