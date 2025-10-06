# 🧪 Catga API 实战测试指南

## 🚀 服务运行状态检查

OrderApi 服务应该正在运行中！让我们进行实际的API测试。

---

## 📡 API 测试命令

### 🎯 **测试 1: 创建订单**

```bash
# PowerShell 测试命令
$headers = @{
    'Content-Type' = 'application/json'
}

$body = @{
    customerId = "CUST-001"
    productId = "PROD-001"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7001/api/orders" -Method Post -Headers $headers -Body $body -SkipCertificateCheck
```

### 🔍 **测试 2: 查询订单**
```bash
# 使用上面返回的订单ID
Invoke-RestMethod -Uri "https://localhost:7001/api/orders/[订单ID]" -Method Get -SkipCertificateCheck
```

### 🚫 **测试 3: 错误场景 - 库存不足**
```bash
$bodyError = @{
    customerId = "CUST-002"
    productId = "PROD-001"
    quantity = 999  # 超过库存
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7001/api/orders" -Method Post -Headers $headers -Body $bodyError -SkipCertificateCheck
```

### 🔴 **测试 4: 错误场景 - 产品不存在**
```bash
$bodyNotFound = @{
    customerId = "CUST-003"
    productId = "PROD-999"  # 不存在的产品
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7001/api/orders" -Method Post -Headers $headers -Body $bodyNotFound -SkipCertificateCheck
```

---

## 🎯 预期响应格式

### ✅ **成功创建订单**
```json
{
  "orderId": "A1B2C3D4",
  "totalAmount": 5999.99,
  "status": "Created",
  "createdAt": "2025-10-05T12:30:00Z"
}
```

### ✅ **成功查询订单**
```json
{
  "orderId": "A1B2C3D4",
  "customerId": "CUST-001",
  "productId": "PROD-001",
  "productName": "笔记本电脑",
  "quantity": 1,
  "unitPrice": 5999.99,
  "totalAmount": 5999.99,
  "status": "Created",
  "createdAt": "2025-10-05T12:30:00Z"
}
```

### ❌ **错误响应**
```json
{
  "error": "库存不足"
}
```

---

## 🎮 浏览器测试

### 📊 **Swagger UI**
1. 打开浏览器访问：`https://localhost:7001/swagger`
2. 点击 "POST /api/orders" 展开
3. 点击 "Try it out"
4. 输入测试数据并执行

### 🔍 **直接 GET 请求**
浏览器访问：`https://localhost:7001/api/orders/{订单ID}`

---

## 🏆 Catga 框架特性验证

通过这些测试，你将验证：

### ✅ **CQRS 模式**
- **命令**: CreateOrderCommand → CreateOrderHandler
- **查询**: GetOrderQuery → GetOrderHandler

### ✅ **强类型结果**
- **成功**: CatgaResult<T>.Success()
- **失败**: CatgaResult<T>.Failure()

### ✅ **依赖注入**
- ICatgaMediator 自动注入
- 处理器自动解析

### ✅ **性能表现**
- 注意响应时间（应该非常快！）
- 内存使用效率

---

**🎯 选择你喜欢的测试方式开始体验吧！**
