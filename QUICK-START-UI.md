# 🚀 快速开始 - UI 使用指南

## 启动应用

### 方式 1: 直接运行 (推荐用于快速测试)

```bash
cd examples/OrderSystem.Api
dotnet run
```

### 方式 2: 使用 Aspire (推荐用于完整体验)

```bash
cd examples/OrderSystem.AppHost
dotnet run
```

应用启动后，打开浏览器访问：**http://localhost:5000**

---

## 🌐 OrderSystem 管理界面

访问 **http://localhost:5000** 打开主界面。

### 1. 仪表盘 📊

查看系统概览：
- **今日订单数**
- **总金额**
- **待处理订单**
- **已完成订单**
- **最近订单列表**（最新5条）

### 2. 订单列表 📦

- 查看所有订单
- 按状态筛选（全部/待处理/已确认/已支付/已发货/已取消）
- 点击订单查看详情
- 状态徽章颜色编码

### 3. 创建订单 ➕

**步骤**:
1. 输入客户ID（例如：`CUST-001`）
2. 添加商品：
   - 商品ID（例如：`PROD-001`）
   - 商品名称（例如：`iPhone 15`）
   - 数量
   - 单价
3. 点击 **+ 添加商品** 可添加多个商品
4. 输入收货地址
5. 选择支付方式（支付宝/微信/信用卡）
6. 查看实时计算的总价
7. 点击 **创建订单**

**结果**:
- 成功：显示成功提示，跳转到订单列表
- 失败：显示错误提示

### 4. Demo 演示 🎬

一键运行预设场景：

#### 成功流程 ✅
点击 **▶️ 运行** 按钮（绿色）

**执行步骤**:
1. ✅ 检查库存
2. ✅ 计算总额
3. ✅ 保存订单
4. ✅ 预留库存
5. ✅ 验证支付
6. ✅ 发布事件

**结果**: 显示成功消息和订单ID

#### 失败回滚 ❌
点击 **▶️ 运行** 按钮（红色）

**执行步骤**:
1-4. ✅ 正常执行
5. ❌ 支付验证失败
6. 🔄 释放库存
7. 🔄 删除订单
8. 📢 发布失败事件

**结果**: 显示失败消息和回滚详情

---

## 🐱 Catga 调试器

### 访问方式

**方式 1**: 点击右上角 **🐱 调试器** 按钮（从 OrderSystem UI）

**方式 2**: 直接访问 **http://localhost:5000/debug**

### 功能

#### 1. 消息流 📊

- 查看所有活跃的消息流
- 实时更新（通过 SignalR）
- 显示：
  - 消息类型
  - 关联ID
  - 状态（成功/失败）
  - 执行时间
  - 时间戳
- 点击流查看详情

#### 2. 统计信息 📈

实时显示：
- **总事件数**
- **成功率**（百分比）
- **平均延迟**（毫秒）
- **存储大小**（字节）
- **活跃流数**

#### 3. 时间旅行 ⏮️

**功能**:
- 系统级回放（所有事件）
- 单流回放（特定消息流）
- 回放速度调节（0.1x - 10x）

**使用**:
1. 选择回放模式
2. 调整回放速度
3. 点击 **▶️ 开始回放**

---

## 💡 使用技巧

### 快速测试流程

1. **启动应用**
   ```bash
   cd examples/OrderSystem.Api && dotnet run
   ```

2. **打开浏览器** → http://localhost:5000

3. **运行 Demo**
   - 切换到 **Demo 演示** 标签
   - 点击 **成功流程** 的运行按钮
   - 观察执行步骤和结果

4. **查看订单**
   - 切换到 **订单列表** 标签
   - 查看刚创建的订单

5. **打开调试器**
   - 点击右上角 **🐱 调试器** 按钮
   - 查看消息流和统计信息

### 测试失败场景

1. 切换到 **Demo 演示** 标签
2. 点击 **失败回滚** 的运行按钮
3. 观察回滚过程
4. 打开调试器查看失败事件

### 创建自定义订单

1. 切换到 **创建订单** 标签
2. 填写表单
3. 添加多个商品
4. 提交订单
5. 在订单列表中查看

---

## 🎨 界面特性

### 响应式设计
- 自动适配桌面和移动设备
- 流畅的动画效果
- 现代化的 UI 设计

### 实时更新
- SignalR 实时通信
- 自动刷新数据
- 无需手动刷新页面

### 用户友好
- Toast 通知（成功/失败）
- 加载状态指示
- 清晰的错误消息
- 直观的操作流程

---

## 🔧 技术栈

### OrderSystem UI
- **AlpineJS 3.13** - 响应式框架
- **Tailwind CSS** - 样式框架
- **SignalR 7.0** - 实时通信
- **纯 HTML/JS** - 无需构建

### Debugger UI
- **AlpineJS 3.13** - 响应式框架
- **Tailwind CSS** - 样式框架
- **SignalR 7.0** - 实时通信
- **纯 HTML/JS** - 无需构建

### 优势
- ✅ 零构建步骤
- ✅ 无需 Node.js
- ✅ AOT 兼容
- ✅ 轻量级（15KB）
- ✅ 即时可用

---

## 📱 API 端点

### OrderSystem API

| 端点 | 方法 | 描述 |
|------|------|------|
| `/` | GET | 订单管理 UI |
| `/api/orders` | POST | 创建订单 |
| `/api/orders/{id}` | GET | 查询订单 |
| `/api/customers/{id}/orders` | GET | 客户订单列表 |
| `/demo/order-success` | POST | Demo：成功场景 |
| `/demo/order-failure` | POST | Demo：失败场景 |
| `/demo/compare` | GET | Demo：对比 |
| `/health` | GET | 健康检查 |
| `/swagger` | GET | API 文档 |

### Debugger API

| 端点 | 方法 | 描述 |
|------|------|------|
| `/debug` | GET | 调试器 UI |
| `/debug-api/flows` | GET | 所有消息流 |
| `/debug-api/flows/{id}` | GET | 特定消息流 |
| `/debug-api/stats` | GET | 统计信息 |
| `/debug/hub` | WebSocket | SignalR Hub |

---

## 🎯 下一步

### 学习更多
- 查看 [完整文档](docs/INDEX.md)
- 阅读 [快速开始](docs/QUICK-START.md)
- 探索 [API 参考](docs/QUICK-REFERENCE.md)

### 自定义
- 修改 `wwwroot/index.html` 调整 UI
- 添加自定义端点
- 扩展 Demo 场景

### 升级到 Vue 3
如果需要更强大的功能，可以升级到完整的 Vue 3 方案：
- TypeScript 类型安全
- Element Plus 组件库
- Pinia 状态管理
- ECharts 图表

参考 `UI-IMPLEMENTATION-SUMMARY.md` 中的升级路径。

---

## ❓ 常见问题

### Q: UI 没有显示？
**A**: 确保：
1. 应用已启动（`dotnet run`）
2. 访问正确的 URL（http://localhost:5000）
3. 浏览器支持 JavaScript
4. 检查浏览器控制台是否有错误

### Q: Demo 按钮没有反应？
**A**: 
1. 检查网络连接
2. 查看浏览器控制台错误
3. 确认 API 端点可访问（`/demo/order-success`）

### Q: 调试器显示"未连接"？
**A**:
1. 检查 SignalR 连接
2. 确认 `/debug/hub` 端点可访问
3. 查看浏览器控制台 SignalR 日志

### Q: 如何自定义 UI？
**A**:
1. 编辑 `examples/OrderSystem.Api/wwwroot/index.html`
2. 修改 AlpineJS 数据和方法
3. 调整 Tailwind CSS 类
4. 无需重新构建，刷新浏览器即可

---

**享受使用 Catga OrderSystem！** 🎉

如有问题，请查看 [完整文档](docs/INDEX.md) 或提交 Issue。

