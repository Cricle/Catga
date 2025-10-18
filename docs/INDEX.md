# Catga 完整文档索引

> 欢迎来到 Catga 文档中心！这里包含框架的完整使用指南和API参考。

---

## 🚀 新手入门

如果你是第一次接触 Catga，从这里开始：

1. **[快速开始](./QUICK-START.md)** - 5 分钟上手 Catga
2. **[Quick Reference](./QUICK-REFERENCE.md)** - API 速查表
3. **[OrderSystem 示例](../examples/OrderSystem.Api/README.md)** - 完整的订单系统示例

---

## 📚 核心文档

### API 参考

- **[ICatgaMediator API](./api/mediator.md)** - 核心 Mediator 接口
  - `SendAsync` - 发送命令
  - `PublishAsync` - 发布事件
  - 批量操作和流处理

- **[消息契约](./api/messages.md)** - 消息定义规范
  - `IRequest<TResponse>` - 命令/查询
  - `IEvent` - 事件
  - MemoryPack 序列化

- **[API 总览](./api/README.md)** - 完整 API 列表

### 使用指南

#### 基础功能

- **[自定义错误处理](./guides/custom-error-handling.md)** ⭐ 推荐
  - SafeRequestHandler 使用
  - 自动回滚实现
  - 虚函数重写

- **[序列化配置](./guides/serialization.md)**
  - MemoryPack（AOT 推荐）
  - JSON 序列化
  - 性能对比

- **[分布式 ID 生成](./guides/distributed-id.md)**
  - Snowflake 算法
  - 配置选项
  - 性能优化

#### 高级功能

- **[Source Generator](./guides/source-generator.md)** - 零反射，自动注册
  - `AddGeneratedHandlers()`
  - `AddGeneratedServices()`
  - 编译时代码生成

- **[自动依赖注入](./guides/auto-di-registration.md)**
  - `[CatgaService]` 属性
  - 服务生命周期
  - 接口绑定

- **[Roslyn 分析器](./guides/analyzers.md)**
  - 编译时检测
  - 配置错误预警
  - 最佳实践建议

---

## 🏗️ 架构设计

### 核心架构

- **[架构概览](./architecture/overview.md)** - Catga 整体架构
- **[CQRS 模式](./architecture/cqrs.md)** - 命令查询责任分离
- **[详细架构](./architecture/ARCHITECTURE.md)** - 深入设计细节
- **[职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md)** - 模块职责划分

### 设计模式

- **[分布式事务（Catga Pattern）](./patterns/DISTRIBUTED-TRANSACTION-V2.md)** ⭐ 创新
  - 改进的 Saga 模式
  - 自动补偿
  - 跨服务协调

---

## 🔍 可观测性

### 分布式追踪

- **[分布式追踪指南](./observability/DISTRIBUTED-TRACING-GUIDE.md)** ⭐ 必读
  - W3C Trace Context 传播
  - Correlation ID 管理
  - 跨服务链路追踪
  - 最佳实践

- **[Jaeger 完整指南](./observability/JAEGER-COMPLETE-GUIDE.md)**
  - Jaeger 安装配置
  - UI 搜索技巧
  - 与 Grafana 集成
  - 生产环境部署

### 监控和指标

- **[监控指南](./production/MONITORING-GUIDE.md)**
  - Prometheus 集成
  - Grafana 仪表板
  - 关键指标说明
  - 告警配置

---

## 🚀 部署和生产

### Native AOT

- **[Native AOT 发布指南](./deployment/native-aot-publishing.md)**
  - 配置步骤
  - 发布命令
  - 优化技巧
  - 常见问题

- **[AOT 序列化指南](./aot/serialization-aot-guide.md)**
  - MemoryPack 配置
  - 避免反射
  - Source Generator 使用

### 容器化部署

- **[Kubernetes 部署](./deployment/kubernetes.md)**
  - Helm Charts
  - ConfigMap 配置
  - 健康检查
  - 自动扩展

- **[分布式架构](./distributed/ARCHITECTURE.md)**
  - 微服务拆分
  - 服务发现
  - 负载均衡

---

## 🎨 示例项目

### OrderSystem - 完整订单系统

**主要演示**：
- ✅ 订单创建成功流程
- ❌ 失败自动回滚
- 📢 事件驱动架构
- 🔍 OpenTelemetry 追踪
- 🎯 自定义错误处理

**相关文档**：
- **[OrderSystem API 文档](../examples/OrderSystem.Api/README.md)**
- **[Aspire AppHost 文档](../examples/OrderSystem.AppHost/README.md)**
- **[优雅关闭说明](../examples/OrderSystem.AppHost/README-GRACEFUL.md)**

### 基础示例

- **[基础用法示例](./examples/basic-usage.md)**
  - Hello World
  - 简单命令处理
  - 事件发布

---

## 📊 性能和基准

### 性能文档

- **[性能报告](./PERFORMANCE-REPORT.md)** - 与其他框架对比
- **[基准测试结果](./BENCHMARK-RESULTS.md)** - 详细测试数据

### 关键指标

| 操作 | 平均耗时 | 内存分配 | 吞吐量 |
|------|---------|---------|--------|
| 命令处理 | 17.6 μs | 408 B | 56K QPS |
| 事件发布 | 428 ns | 0 B | 2.3M QPS |
| MemoryPack 序列化 | 48 ns | 0 B | 20M/s |

---

## 📖 参考资料

### 项目信息

- **[项目总结](./PROJECT_SUMMARY.md)** - 功能概览
- **[变更日志](./CHANGELOG.md)** - 版本历史
- **[框架路线图](./FRAMEWORK-ROADMAP.md)** - 未来计划

### 发布管理

- **[发布就绪检查清单](./RELEASE-READINESS-CHECKLIST.md)**
  - 功能完成度
  - 测试覆盖率
  - 文档完整性
  - 性能验证

---

## 🔗 外部资源

### .NET 官方文档

- [.NET 9 新特性](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [Native AOT 官方指南](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [Source Generators 文档](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [OpenTelemetry .NET SDK](https://opentelemetry.io/docs/languages/net/)

### 可观测性工具

- [Jaeger 官方文档](https://www.jaegertracing.io/docs/)
- [Prometheus 文档](https://prometheus.io/docs/)
- [Grafana 文档](https://grafana.com/docs/)
- [OpenTelemetry 规范](https://opentelemetry.io/docs/specs/otel/)

### 相关项目

- [MediatR](https://github.com/jbogard/MediatR) - CQRS 灵感来源
- [MassTransit](https://github.com/MassTransit/MassTransit) - 分布式消息
- [MemoryPack](https://github.com/Cysharp/MemoryPack) - 高性能序列化
- [NATS](https://nats.io/) - 消息系统

---

## 🤝 贡献和支持

### 参与贡献

- **[贡献指南](../CONTRIBUTING.md)** - 如何为 Catga 做贡献

### 获取帮助

- **GitHub Issues** - 提交 Bug 或功能请求
- **GitHub Discussions** - 社区讨论
- **示例项目** - 查看完整的可运行示例

---

## 📑 文档导航

### 按角色导航

#### 🆕 新用户

1. 阅读 [README.md](../README.md)
2. 跟随 [快速开始](./QUICK-START.md)
3. 运行 [OrderSystem 示例](../examples/OrderSystem.Api/README.md)

#### 💻 开发者

1. 查看 [API 参考](./api/README.md)
2. 学习 [自定义错误处理](./guides/custom-error-handling.md)
3. 了解 [Source Generator](./guides/source-generator.md)

#### 🏗️ 架构师

1. 阅读 [架构概览](./architecture/overview.md)
2. 理解 [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md)
3. 研究 [性能报告](./PERFORMANCE-REPORT.md)

#### 🚀 运维工程师

1. 学习 [Native AOT 发布](./deployment/native-aot-publishing.md)
2. 配置 [Kubernetes 部署](./deployment/kubernetes.md)
3. 设置 [监控系统](./production/MONITORING-GUIDE.md)

### 按场景导航

#### 🎯 快速开发

- [快速开始](./QUICK-START.md)
- [Quick Reference](./QUICK-REFERENCE.md)
- [基础用法](./examples/basic-usage.md)

#### 🔍 调试和追踪

- [分布式追踪指南](./observability/DISTRIBUTED-TRACING-GUIDE.md)
- [Jaeger 完整指南](./observability/JAEGER-COMPLETE-GUIDE.md)
- [监控指南](./production/MONITORING-GUIDE.md)

#### 🚀 生产部署

- [Native AOT 发布](./deployment/native-aot-publishing.md)
- [Kubernetes 部署](./deployment/kubernetes.md)
- [性能优化](./PERFORMANCE-REPORT.md)

#### 🏗️ 架构设计

- [CQRS 模式](./architecture/cqrs.md)
- [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md)
- [分布式架构](./distributed/ARCHITECTURE.md)

---

<div align="center">

**📚 文档持续完善中**

如有任何疑问或建议，欢迎提交 [Issue](https://github.com/your-org/Catga/issues)

[返回首页](../README.md) · [查看示例](../examples/README.md) · [贡献指南](../CONTRIBUTING.md)

</div>
