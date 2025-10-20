# Catga 文档中心

欢迎来到 Catga 框架文档中心！这里包含了所有你需要的文档资源。

---

## 🚀 快速导航

### 新手入门

- [**快速开始**](./articles/getting-started.md) - 5 分钟快速上手
- [**架构概览**](./articles/architecture.md) - 深入理解 Catga 架构
- [**配置指南**](./articles/configuration.md) - 完整配置选项

### 核心概念

- [**CQRS 模式**](./architecture/cqrs.md) - Command/Query 分离原理
- [**架构设计**](./architecture/ARCHITECTURE.md) - 系统架构设计思路
- [**职责边界**](./architecture/RESPONSIBILITY-BOUNDARY.md) - 组件职责划分

### API 参考

- [**Mediator API**](./api/mediator.md) - `ICatgaMediator` 接口文档
- [**消息定义**](./api/messages.md) - `IRequest` / `INotification` 接口
- [**API 索引**](./api/README.md) - 完整 API 列表

---

## 📖 使用指南

### 基础功能

- [**序列化配置**](./guides/serialization.md) - JSON/MemoryPack 序列化器
- [**Source Generator**](./guides/source-generator.md) - 自动代码生成
- [**自动注册**](./guides/auto-di-registration.md) - DI 自动注册
- [**错误处理**](./guides/custom-error-handling.md) - 异常处理最佳实践
- [**分布式 ID**](./guides/distributed-id.md) - 分布式 ID 生成

### AOT 支持

- [**AOT 部署**](./articles/aot-deployment.md) - Native AOT 编译发布
- [**AOT 序列化**](./aot/serialization-aot-guide.md) - AOT 兼容序列化指南

### 代码分析器

- [**分析器使用**](./analyzers/README.md) - Roslyn 分析器
- [**分析器指南**](./guides/analyzers.md) - 代码质量分析

---

## 🌐 分布式系统

### 架构设计

- [**分布式架构**](./distributed/ARCHITECTURE.md) - 分布式系统设计
- [**分布式概览**](./distributed/README.md) - 分布式功能介绍

### 部署

- [**Native AOT 发布**](./deployment/native-aot-publishing.md) - AOT 编译发布流程
- [**Kubernetes 部署**](./deployment/kubernetes.md) - K8s 部署指南
- [**Kubernetes 架构**](./distributed/KUBERNETES.md) - K8s 架构设计

### 高级模式

- [**分布式事务**](./patterns/DISTRIBUTED-TRANSACTION-V2.md) - Catga 事务模式

---

## 🔍 可观测性

### 追踪和监控

- [**OpenTelemetry 集成**](./articles/opentelemetry-integration.md) - 分布式追踪集成
- [**分布式追踪指南**](./observability/DISTRIBUTED-TRACING-GUIDE.md) - 跨服务链路追踪
- [**Jaeger 完整指南**](./observability/JAEGER-COMPLETE-GUIDE.md) - Jaeger 链路搜索
- [**监控指南**](./production/MONITORING-GUIDE.md) - Prometheus/Grafana 监控

---

## 📊 性能

### 基准测试

- [**性能报告**](./PERFORMANCE-REPORT.md) - 完整性能基准测试
- [**基准测试结果**](./BENCHMARK-RESULTS.md) - 详细测试数据

---

## 📚 示例代码

### 基础示例

- [**基础用法**](./examples/basic-usage.md) - 基本使用示例

### 完整项目

参见根目录的 [`examples/`](../examples/) 文件夹：

- **MinimalApi** - 最简单的示例
- **OrderSystem** - 完整的订单系统

---

## 📝 参考资料

### 完整索引

- [**文档索引**](./INDEX.md) - 所有文档的完整列表
- [**更新日志**](./CHANGELOG.md) - 版本更新记录

### 网站

- [**官方网站**](https://cricle.github.io/Catga/) - 在线文档和演示
- [**AI 学习指南**](../AI-LEARNING-GUIDE.md) - 专为 AI 助手设计的学习资料

---

## 🔗 外部资源

- [**GitHub 仓库**](https://github.com/Cricle/Catga) - 源代码和问题追踪
- [**NuGet 包**](https://www.nuget.org/packages/Catga) - NuGet 包列表
- [**GitHub Discussions**](https://github.com/Cricle/Catga/discussions) - 社区讨论

---

## 📂 文档结构

```
docs/
├── README.md                    # 本文件 - 文档中心
├── INDEX.md                     # 完整文档索引
├── CHANGELOG.md                 # 更新日志
│
├── articles/                    # 入门文章
│   ├── getting-started.md       # 快速开始
│   ├── architecture.md          # 架构概览
│   ├── configuration.md         # 配置指南
│   ├── aot-deployment.md        # AOT 部署
│   └── opentelemetry-integration.md  # OpenTelemetry 集成
│
├── architecture/                # 架构设计
│   ├── ARCHITECTURE.md          # 架构设计文档
│   ├── cqrs.md                  # CQRS 模式
│   ├── overview.md              # 架构概览
│   └── RESPONSIBILITY-BOUNDARY.md  # 职责边界
│
├── api/                         # API 参考
│   ├── README.md                # API 索引
│   ├── mediator.md              # Mediator API
│   └── messages.md              # 消息定义
│
├── guides/                      # 使用指南
│   ├── serialization.md         # 序列化配置
│   ├── source-generator.md      # Source Generator
│   ├── auto-di-registration.md  # 自动注册
│   ├── custom-error-handling.md # 错误处理
│   ├── distributed-id.md        # 分布式 ID
│   └── analyzers.md             # 代码分析器
│
├── aot/                         # AOT 支持
│   └── serialization-aot-guide.md  # AOT 序列化指南
│
├── distributed/                 # 分布式系统
│   ├── README.md                # 分布式概览
│   ├── ARCHITECTURE.md          # 分布式架构
│   └── KUBERNETES.md            # K8s 架构
│
├── deployment/                  # 部署
│   ├── native-aot-publishing.md # AOT 发布
│   └── kubernetes.md            # K8s 部署
│
├── patterns/                    # 设计模式
│   └── DISTRIBUTED-TRANSACTION-V2.md  # 分布式事务
│
├── observability/               # 可观测性
│   ├── DISTRIBUTED-TRACING-GUIDE.md   # 分布式追踪
│   └── JAEGER-COMPLETE-GUIDE.md       # Jaeger 指南
│
├── production/                  # 生产环境
│   └── MONITORING-GUIDE.md      # 监控指南
│
├── examples/                    # 示例代码
│   └── basic-usage.md           # 基础用法
│
├── analyzers/                   # 代码分析器
│   └── README.md                # 分析器文档
│
├── web/                         # 官方网站
│   └── index.html               # 网站首页
│
├── PERFORMANCE-REPORT.md        # 性能报告
├── BENCHMARK-RESULTS.md         # 基准测试结果
└── toc.yml                      # DocFX 目录配置
```

---

## 🎯 推荐学习路径

### 初学者

1. 阅读 [快速开始](./articles/getting-started.md)
2. 查看 [基础用法示例](./examples/basic-usage.md)
3. 运行 [MinimalApi 示例](../examples/MinimalApi/)
4. 理解 [CQRS 模式](./architecture/cqrs.md)

### 中级开发者

1. 深入 [架构设计](./architecture/ARCHITECTURE.md)
2. 学习 [Source Generator](./guides/source-generator.md)
3. 配置 [序列化器](./guides/serialization.md)
4. 实现 [错误处理](./guides/custom-error-handling.md)
5. 运行 [OrderSystem 示例](../examples/OrderSystem.Api/)

### 高级开发者

1. 部署 [Native AOT](./deployment/native-aot-publishing.md)
2. 设计 [分布式架构](./distributed/ARCHITECTURE.md)
3. 实现 [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md)
4. 配置 [分布式追踪](./observability/DISTRIBUTED-TRACING-GUIDE.md)
5. 优化 [性能](./PERFORMANCE-REPORT.md)

---

## 💡 获取帮助

如果在文档中找不到答案，可以：

1. 📝 查看 [GitHub Issues](https://github.com/Cricle/Catga/issues) - 已知问题和解决方案
2. 💬 参与 [GitHub Discussions](https://github.com/Cricle/Catga/discussions) - 提问和讨论
3. 📖 阅读 [AI 学习指南](../AI-LEARNING-GUIDE.md) - AI 助手的完整指南
4. 🔍 搜索 [完整文档索引](./INDEX.md) - 查找特定主题

---

<div align="center">

**📚 快乐学习 Catga！**

[⬆ 回到顶部](#catga-文档中心)

</div>

