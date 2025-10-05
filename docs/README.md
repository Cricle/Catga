# 📚 Catga 框架文档中心

> 完整的 Catga 分布式 CQRS 框架学习和参考资源

## 🚀 快速导航

<div align="center">

| 🎯 我想... | 📖 推荐文档 | ⏱️ 预计时间 |
|-----------|------------|----------|
| **快速上手** | [5分钟快速开始](guides/quick-start.md) | 5分钟 |
| **理解架构** | [架构概览](architecture/overview.md) | 15分钟 |
| **查看示例** | [完整示例](../examples/README.md) | 30分钟 |
| **API 参考** | [API 文档](api/README.md) | 按需查阅 |

</div>

## 📋 文档结构

### 🎓 学习指南
```
guides/
├── 🚀 quick-start.md          # 5分钟快速开始
├── 📦 installation.md         # 详细安装指南
├── ⚙️ configuration.md        # 配置选项详解
├── 📝 commands.md             # 命令处理指南
├── 🔍 queries.md              # 查询处理指南
├── 📢 events.md               # 事件发布指南
├── 🔄 distributed-transactions.md  # 分布式事务
├── 🔒 idempotency.md          # 幂等性处理
├── 🛡️ resilience.md           # 弹性机制
├── 🌐 nats-transport.md       # NATS 消息传递
├── 🗄️ redis-persistence.md    # Redis 持久化
├── 🔧 custom-behaviors.md     # 自定义管道行为
├── ⚡ performance.md          # 性能优化
├── 👁️ observability.md        # 可观测性
├── 🏗️ aot-deployment.md       # AOT 部署
└── ✨ best-practices.md       # 最佳实践
```

### 🏗️ 架构文档
```
architecture/
├── 🔍 overview.md             # 系统架构概览
├── 🎯 cqrs.md                 # CQRS 模式详解
├── 🔄 catga-transactions.md   # CatGa 分布式事务
├── 🔗 pipeline-behaviors.md   # 管道行为机制
├── 📊 performance-design.md   # 性能设计原理
└── 🌐 distributed-patterns.md # 分布式模式
```

### 📖 API 参考
```
api/
├── 📋 README.md               # API 文档入口
├── 🎛️ mediator.md             # ICatgaMediator 接口
├── 📨 messages.md             # 消息类型定义
├── 🔧 handlers.md             # 处理器接口
├── 📊 results.md              # 结果类型
├── ⚠️ exceptions.md           # 异常处理
├── 🔄 pipeline-behaviors.md   # 管道行为 API
└── 🌐 extensions.md           # 扩展组件 API
```

### 💡 实用示例
```
examples/
├── 📦 basic-usage.md          # 基础用法示例
├── 🌐 microservices.md        # 微服务架构
├── 📢 event-driven.md         # 事件驱动系统
├── 🔄 saga-patterns.md        # Saga 事务模式
└── 🚀 production-ready.md     # 生产环境配置
```

## 🎯 学习路径

### 🔰 初学者路径 (30分钟)
1. **📚 理解概念** → [CQRS 模式](architecture/cqrs.md) (10分钟)
2. **🚀 快速开始** → [5分钟教程](guides/quick-start.md) (5分钟)
3. **💡 查看示例** → [基础用法](examples/basic-usage.md) (15分钟)

### 🎯 进阶开发者 (1小时)
1. **🏗️ 系统架构** → [架构概览](architecture/overview.md) (20分钟)
2. **🔗 管道机制** → [Pipeline 行为](architecture/pipeline-behaviors.md) (15分钟)
3. **🛡️ 弹性设计** → [弹性机制](guides/resilience.md) (15分钟)
4. **🔧 自定义扩展** → [自定义行为](guides/custom-behaviors.md) (10分钟)

### 🌐 分布式专家 (2小时)
1. **🔄 分布式事务** → [CatGa 事务](architecture/catga-transactions.md) (30分钟)
2. **📡 消息传递** → [NATS 集成](guides/nats-transport.md) (30分钟)
3. **🗄️ 状态管理** → [Redis 持久化](guides/redis-persistence.md) (30分钟)
4. **🏢 微服务实战** → [微服务示例](examples/microservices.md) (30分钟)

### ⚡ 性能优化师 (1.5小时)
1. **📊 性能设计** → [性能架构](architecture/performance-design.md) (30分钟)
2. **⚡ 优化指南** → [性能优化](guides/performance.md) (30分钟)
3. **🏗️ AOT 部署** → [原生编译](guides/aot-deployment.md) (30分钟)

## 🔍 快速查找

### 按功能查找
- **命令处理**: [commands.md](guides/commands.md) • [handlers.md](api/handlers.md)
- **查询处理**: [queries.md](guides/queries.md) • [handlers.md](api/handlers.md)
- **事件发布**: [events.md](guides/events.md) • [messages.md](api/messages.md)
- **错误处理**: [exceptions.md](api/exceptions.md) • [results.md](api/results.md)
- **分布式**: [distributed-transactions.md](guides/distributed-transactions.md) • [nats-transport.md](guides/nats-transport.md)

### 按场景查找
- **Web API**: [OrderApi 示例](../examples/OrderApi/README.md)
- **微服务**: [分布式示例](../examples/NatsDistributed/README.md)
- **高性能**: [性能优化](guides/performance.md) • [AOT 部署](guides/aot-deployment.md)
- **生产环境**: [最佳实践](guides/best-practices.md) • [可观测性](guides/observability.md)

## 🛠️ 实用工具

### 📊 性能基准
```bash
# 运行性能测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks

# 查看详细报告
./benchmarks/run-benchmarks.ps1  # Windows
./benchmarks/run-benchmarks.sh   # Linux/macOS
```

### 🎮 演示脚本
```bash
# 完整演示
./demo.ps1        # Windows
./demo.sh         # Linux/macOS

# 仅运行示例
./demo.ps1 -RunExamples
./demo.sh --run-examples
```

### 🧪 单元测试
```bash
# 运行所有测试
dotnet test

# 生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

## 🤝 文档贡献

### 如何贡献
1. **🍴 Fork** 项目 → [GitHub 仓库](https://github.com/your-org/Catga)
2. **📝 编辑** 文档 → 使用 Markdown 格式
3. **✅ 预览** 更改 → 本地预览确保格式正确
4. **📤 提交** PR → 描述你的改进

### 文档规范
- ✅ 使用清晰的标题结构 (H1-H6)
- ✅ 添加代码示例和说明
- ✅ 包含必要的链接引用
- ✅ 保持简洁明了的语言
- ✅ 添加适当的表情符号 🎯

### 本地预览
```bash
# 安装 Markdown 预览工具
npm install -g @marp-team/marp-cli

# 预览文档
marp docs/ --preview
```

## 📞 获取帮助

### 🆘 遇到问题？
- 🔍 **搜索文档**: 使用 Ctrl+F 在页面内搜索
- 💬 **社区讨论**: [GitHub Discussions](https://github.com/your-org/Catga/discussions)
- 🐛 **报告问题**: [GitHub Issues](https://github.com/your-org/Catga/issues)
- 📧 **技术支持**: support@catga.dev

### 🌟 想要更多？
- 📺 **视频教程**: [YouTube 频道](https://youtube.com/@catga-framework)
- 💬 **即时聊天**: [Discord 社区](https://discord.gg/catga)
- 📱 **关注动态**: [@CatgaFramework](https://twitter.com/CatgaFramework)

## 📈 文档统计

- 📄 **总文档数**: 25+ 篇
- 🎯 **覆盖功能**: 100% API 覆盖
- 💡 **示例代码**: 50+ 个实用示例
- 🌐 **多语言**: 中文 + English (计划中)
- 📱 **移动友好**: 响应式设计

---

<div align="center">

**📚 开始你的 Catga 学习之旅！**

[🚀 快速开始](guides/quick-start.md) • [🏗️ 查看架构](architecture/overview.md) • [💡 浏览示例](../examples/README.md)

*构建更好的分布式系统，从这里开始* ✨

</div>

