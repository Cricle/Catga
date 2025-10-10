# RedisExample - Catga 订单管理示例

## 📖 简介

订单管理示例，演示 Catga 的：
- ✨ **源生成器自动注册** - 零手动配置
- 🆔 **分布式 ID 生成** - 全局唯一订单 ID
- 📝 **CQRS 模式** - Command/Query 分离

> 💡 Redis 分布式锁和缓存功能正在开发中...

## 🚀 快速开始

### 运行示例

```bash
cd examples/RedisExample
dotnet run
```

访问 Swagger: `https://localhost:5001/swagger`

### 测试 API

**创建订单**:
```bash
curl -X POST https://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": "PROD-001", "quantity": 2}'
```

**查询订单**:
```bash
curl https://localhost:5001/orders/123
```

## 🎯 核心特性

### 1. 源生成器自动注册

```csharp
// ✨ 只需调用一次
builder.Services.AddGeneratedHandlers();  // 自动发现并注册所有 Handler
```

**所有 Handler 自动发现，无需手动注册！**

### 2. 分布式 ID 生成

```csharp
// 🆔 启用分布式 ID
builder.Services.AddDistributedId();
```

**特点**:
- ✅ 全局唯一
- ✅ 趋势递增
- ✅ 高性能（4.1M IDs/秒）
- ✅ 0 GC 压力

## 📊 性能

- **创建订单**: ~2ms
- **查询订单**: ~1ms
- **ID 生成**: ~241ns

## 📚 相关文档

- [Catga 快速开始](../../QUICK_START.md)
- [架构说明](../../ARCHITECTURE.md)
