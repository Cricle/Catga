# 📚 Catga 完整文档

欢迎来到 Catga 框架的完整文档中心！

---

## 🚀 快速入门

### 新手必读
- **[快速入门指南](QuickStart.md)** - 5分钟上手，从零开始构建您的第一个 CQRS 应用
- **[架构概览](Architecture.md)** - 理解 Catga 的核心设计和架构原理
- **[基础示例](examples/basic-usage.md)** - 常见使用场景和代码示例

---

## 📖 核心文档

### 架构与设计
- **[架构文档](architecture/ARCHITECTURE.md)** - 完整的架构设计文档
- **[CQRS 模式](architecture/cqrs.md)** - 命令查询职责分离模式详解
- **[架构概览](architecture/overview.md)** - 系统整体架构说明

### API 参考
- **[Mediator API](api/mediator.md)** - 核心 Mediator 接口和使用方法
- **[消息定义](api/messages.md)** - Command、Query、Event 消息定义
- **[API 总览](api/README.md)** - 完整 API 参考索引

---

## 🛠️ 工具链

### 源生成器
- **[源生成器指南](guides/source-generator.md)** - 自动化 Handler 注册的魔法
  - 零反射，100% AOT 兼容
  - 编译时发现所有 Handler
  - 自动生成注册代码

### 代码分析器
- **[分析器完整指南](guides/analyzers.md)** - 15 条规则 + 9 个自动修复
  - Handler 正确性检查
  - 性能最佳实践
  - AOT 兼容性检查

---

## 🌐 分布式与集群

### 分布式架构
- **[分布式概览](distributed/README.md)** - 分布式系统架构总览
  - NATS 传输层
  - Redis 持久化层
  - P2P 集群架构

### 可靠性模式
- **[Outbox/Inbox 模式](patterns/outbox-inbox.md)** - 可靠消息投递
  - At-least-once 语义
  - 幂等性保证
  - 分布式事务

---

## ⚡ 性能优化

### 性能调优
- **[性能调优指南](PerformanceTuning.md)** - 极致性能优化技巧
  - FastPath 优化
  - 内存池化
  - 并发控制

### 基准测试
- **[基准测试报告](benchmarks/BASELINE_REPORT.md)** - 详细的性能基准数据
  - vs MediatR (2.6x)
  - vs MassTransit (70x 启动速度)
  - 内存分配分析

### 性能文档
- **[性能总览](performance/README.md)** - 性能优化历程和成果

---

## 🎯 AOT 兼容性

### Native AOT
- **[Native AOT 指南](aot/native-aot-guide.md)** - 100% AOT 兼容指南
  - 零反射设计
  - 静态分析友好
  - 跨平台部署

- **[AOT 最佳实践](aot/AOT_BEST_PRACTICES.md)** - 生产环境实战经验
  - 常见陷阱避免
  - 性能优化技巧
  - 部署建议

---

## 📊 可观测性

### 监控与追踪
- **[可观测性指南](observability/README.md)** - OpenTelemetry 集成
  - Metrics 指标
  - Tracing 追踪
  - Logging 日志
  - 健康检查

---

## 🏗️ 生产最佳实践

### 最佳实践
- **[最佳实践指南](BestPractices.md)** - 生产级应用开发指南
  - 错误处理
  - 事务管理
  - 性能优化
  - 安全性考虑

### 迁移指南
- **[迁移指南](Migration.md)** - 从其他框架迁移到 Catga
  - 从 MediatR 迁移
  - 从 MassTransit 迁移
  - 兼容性对照表

### 序列化
- **[序列化指南](serialization/README.md)** - 序列化配置和最佳实践
  - System.Text.Json
  - MemoryPack
  - 性能对比

---

## 📁 文档结构

```
docs/
├── QuickStart.md              # 快速入门
├── Architecture.md            # 架构指南
├── PerformanceTuning.md       # 性能调优
├── BestPractices.md           # 最佳实践
├── Migration.md               # 迁移指南
│
├── architecture/              # 架构文档
│   ├── ARCHITECTURE.md
│   ├── cqrs.md
│   └── overview.md
│
├── api/                       # API 参考
│   ├── mediator.md
│   ├── messages.md
│   └── README.md
│
├── guides/                    # 使用指南
│   ├── source-generator.md
│   └── analyzers.md
│
├── distributed/               # 分布式
│   └── README.md
│
├── patterns/                  # 设计模式
│   └── outbox-inbox.md
│
├── performance/               # 性能文档
│   └── README.md
│
├── benchmarks/                # 基准测试
│   └── BASELINE_REPORT.md
│
├── aot/                       # AOT 文档
│   ├── native-aot-guide.md
│   └── AOT_BEST_PRACTICES.md
│
├── observability/             # 可观测性
│   └── README.md
│
├── serialization/             # 序列化
│   └── README.md
│
└── examples/                  # 示例代码
    └── basic-usage.md
```

---

## 🎯 按场景查找文档

### 我是新手
1. [快速入门](QuickStart.md)
2. [基础示例](examples/basic-usage.md)
3. [架构概览](Architecture.md)

### 我要优化性能
1. [性能调优](PerformanceTuning.md)
2. [基准测试](benchmarks/BASELINE_REPORT.md)
3. [AOT 最佳实践](aot/AOT_BEST_PRACTICES.md)

### 我要构建分布式系统
1. [分布式概览](distributed/README.md)
2. [Outbox/Inbox 模式](patterns/outbox-inbox.md)
3. [可观测性](observability/README.md)

### 我要从其他框架迁移
1. [迁移指南](Migration.md)
2. [API 参考](api/README.md)
3. [最佳实践](BestPractices.md)

### 我要使用源生成器
1. [源生成器指南](guides/source-generator.md)
2. [分析器指南](guides/analyzers.md)
3. [AOT 指南](aot/native-aot-guide.md)

---

## 🔗 外部资源

- **[GitHub 仓库](https://github.com/你的用户名/Catga)** - 源代码和 Issue 跟踪
- **[示例项目](../examples/)** - 完整的示例代码
  - SimpleWebApi - 基础 Web API + Saga 示例
  - DistributedCluster - 分布式集群示例

---

## 📝 文档贡献

发现文档问题或有改进建议？欢迎提交 PR！

1. Fork 仓库
2. 编辑文档
3. 提交 Pull Request

---

## 📞 获取帮助

- **GitHub Issues** - 报告问题
- **GitHub Discussions** - 讨论和提问
- **贡献指南** - 参与贡献

---

**让我们开始使用 Catga 吧！** 🚀

