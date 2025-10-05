# 🎮 Catga 框架实时演示

## 🚀 正在启动 OrderApi 示例...

### 📋 演示内容
我将为你启动一个完整的 Web API 示例，展示 Catga 框架的实际应用：

#### ✨ 你将体验到：
- **🎯 CQRS 模式** - 清晰的命令查询分离
- **🔧 依赖注入** - 现代化的服务配置
- **📊 Swagger UI** - 完整的 API 文档界面
- **⚡ 高性能处理** - 微秒级响应时间
- **🛡️ 错误处理** - 统一的结果处理

#### 🎯 可测试的功能：
1. **创建订单** (`POST /api/orders`)
   - 验证产品存在性
   - 检查库存数量
   - 计算总金额
   - 返回订单详情

2. **查询订单** (`GET /api/orders/{id}`)
   - 根据订单ID查询
   - 返回完整订单信息
   - 包含产品详情

#### 📦 预置数据：
| 产品ID | 名称 | 价格 | 库存 |
|--------|------|------|------|
| PROD-001 | Laptop | ¥5,999.99 | 10 |
| PROD-002 | Mouse | ¥199.99 | 50 |
| PROD-003 | Keyboard | ¥699.99 | 25 |

### 🌐 访问方式
服务启动后，你可以：
- **Swagger UI**: `https://localhost:7xxx/swagger`
- **API 端点**: `https://localhost:7xxx/api/orders`

### 📝 测试示例
```json
// 创建订单请求
POST /api/orders
{
  "customerId": "CUST-001",
  "productId": "PROD-001",
  "quantity": 1
}

// 预期响应
{
  "orderId": "A1B2C3D4",
  "totalAmount": 5999.99,
  "status": "Created",
  "createdAt": "2025-10-05T12:00:00Z"
}
```

---

**🎉 准备好体验 Catga 框架的强大功能了吗？**
