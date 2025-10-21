# Catga 文档索引

完整的 Catga 文档导航

---

## 📖 入门指南

### 基础教程
- [快速开始](./articles/getting-started.md) - 5分钟快速入门
- [架构概览](./architecture/overview.md) - 了解 Catga 架构
- [配置指南](./articles/configuration.md) - 详细配置说明
- [基础示例](./examples/basic-usage.md) - 基本使用示例

---

## 🏗️ 架构设计

### 核心架构
- [架构文档](./architecture/ARCHITECTURE.md) - 整体架构设计
- [CQRS 模式](./architecture/cqrs.md) - CQRS 实现详解
- [职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md) - 组件职责划分

---

## 🎯 核心功能

### 消息处理
- [Mediator API](./api/mediator.md) - 中介者模式 API
- [消息定义](./api/messages.md) - 消息类型说明
- [API 参考](./api/README.md) - 完整 API 文档

### 分布式特性
- [分布式 ID](./guides/distributed-id.md) - 高性能 ID 生成器
- [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md) - Saga/Outbox 模式
- [消息序列化](./guides/serialization.md) - JSON/MemoryPack 对比

### Source Generator
- [Source Generator 使用](./guides/source-generator-usage.md) - 快速上手
- [Source Generator 指南](./guides/source-generator.md) - 深入理解
- [自动 DI 注册](./guides/auto-di-registration.md) - 自动依赖注入

---

## 🚀 高级主题

### 性能优化
- [性能基准测试](./BENCHMARK-RESULTS.md) - **纳秒级延迟 (400-600ns), 2M+ QPS 吞吐量**
- [内存优化计划](./guides/memory-optimization-plan.md) - 零分配优化
- [内存优化指南](./guides/memory-optimization-guide.md) - 实战技巧
- [性能报告](./PERFORMANCE-REPORT.md) - 详细性能分析
- [GC 和热路径优化](./development/GC_AND_HOTPATH_REVIEW.md) - TagList 栈分配, Span 优化
- [线程池管理](./development/THREAD_POOL_MANAGEMENT_PLAN.md) - 并发限制, 熔断器, 批处理

### Native AOT
- [AOT 部署](./articles/aot-deployment.md) - Native AOT 部署
- [AOT 发布](./deployment/native-aot-publishing.md) - AOT 编译配置
- [序列化 AOT 指南](./aot/serialization-aot-guide.md) - AOT 友好序列化

### 可观测性
- [分布式追踪指南](./observability/DISTRIBUTED-TRACING-GUIDE.md) - OpenTelemetry 集成
- [Jaeger 完整指南](./observability/JAEGER-COMPLETE-GUIDE.md) - Jaeger 配置
- [OpenTelemetry 集成](./articles/opentelemetry-integration.md) - OTEL 实践
- [监控指南](./production/MONITORING-GUIDE.md) - 生产环境监控

---

## 🔧 开发者工具

### 代码分析
- [Analyzers 指南](./guides/analyzers.md) - 代码分析器使用
- [Analyzers 文档](./analyzers/README.md) - 分析器详细说明

### 错误处理
- [自定义错误处理](./guides/custom-error-handling.md) - 错误处理策略

---

## 🌐 部署运维

### Kubernetes
- [Kubernetes 部署](./deployment/kubernetes.md) - K8s 部署指南
- [生产环境监控](./production/MONITORING-GUIDE.md) - 监控最佳实践

---

## 📚 其他资源

### 品牌资源
- [Logo 设计指南](./branding/logo-guide.md) - Logo 使用规范

### 项目管理
- [更新日志](./CHANGELOG.md) - 版本变更记录

### Web 资源
- [Web 文档](./web/README.md) - 在线文档站点

---

## 🔗 快速链接

- **官方网站**: [https://cricle.github.io/Catga/](https://cricle.github.io/Catga/)
- **API 在线文档**: [https://cricle.github.io/Catga/api.html](https://cricle.github.io/Catga/api.html)
- **GitHub**: [https://github.com/Cricle/Catga](https://github.com/Cricle/Catga)
- **示例代码**: [../examples/](../examples/)
- **AI 学习指南**: [../AI-LEARNING-GUIDE.md](../AI-LEARNING-GUIDE.md)

---

## 📝 文档贡献

发现文档问题或想要改进？

1. Fork 项目
2. 编辑文档
3. 提交 Pull Request

查看 [CONTRIBUTING.md](../CONTRIBUTING.md) 了解详情。

---

**Made with ❤️ by Catga Contributors**

