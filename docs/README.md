# Catga 文档

欢迎来到 Catga 文档！Catga 是一个简洁、高性能的 .NET CQRS 框架。

---

## 📚 文档导航

### 🚀 开始使用

- [快速开始](articles/getting-started.md) - 5 分钟上手 Catga
- [基础示例](examples/basic-usage.md) - 基本用法示例
- [配置选项](articles/configuration.md) - 详细配置说明

### 🏗️ 架构设计

- [架构概览](architecture/ARCHITECTURE.md) - 核心设计理念
- [CQRS 模式](architecture/cqrs.md) - CQRS 实现详解
- [职责边界](architecture/RESPONSIBILITY-BOUNDARY.md) - 组件职责划分

### 📖 使用指南

- [错误处理](guides/error-handling.md) - CatgaResult 和错误码
- [内存优化](guides/memory-optimization-guide.md) - 性能优化技巧
- [序列化](guides/serialization.md) - 序列化器使用
- [分布式 ID](guides/distributed-id.md) - Snowflake ID 生成器
- [Source Generator](guides/source-generator.md) - 源代码生成器
- [代码分析器](guides/analyzers.md) - 编译时检查

### 🌐 可观测性

- [分布式追踪](observability/DISTRIBUTED-TRACING-GUIDE.md) - OpenTelemetry 集成
- [Jaeger 集成](observability/JAEGER-COMPLETE-GUIDE.md) - Jaeger 完整指南
- [监控指南](production/MONITORING-GUIDE.md) - 生产环境监控

### 🚀 部署

- [Kubernetes](deployment/kubernetes.md) - K8s 部署指南
- [Native AOT](deployment/native-aot-publishing.md) - AOT 发布
- [AOT 序列化](aot/serialization-aot-guide.md) - AOT 序列化指南

### 📊 性能

- [性能报告](PERFORMANCE-REPORT.md) - 基准测试结果
- [基准测试](BENCHMARK-RESULTS.md) - 详细基准数据

### 📝 API 文档

- [API 概览](api/README.md) - API 参考
- [Mediator API](api/mediator.md) - ICatgaMediator 接口
- [消息 API](api/messages.md) - 消息定义

---

## 🎯 设计哲学

**Simple > Perfect**
- 6 个核心文件夹（从 14 个精简）
- 10 个错误代码（从 50+ 精简）
- 删除 50+ 未使用的抽象

**Focused > Comprehensive**
- 专注 CQRS 核心功能
- 删除 RPC、Cache、Lock 等过度设计
- 保持 API 最小化

**Fast > Feature-Rich**
- Command/Query < 1μs
- Event 广播 < 500ns
- 零分配优化
- AOT 兼容

---

## 📦 核心包

| 包名 | 功能 |
|------|------|
| `Catga` | 核心框架 |
| `Catga.Serialization.Json` | JSON 序列化（AOT 优化）|
| `Catga.Serialization.MemoryPack` | 高性能二进制序列化 |
| `Catga.Transport.InMemory` | 进程内传输 |
| `Catga.Transport.Redis` | Redis 传输 |
| `Catga.Transport.Nats` | NATS 传输 |
| `Catga.Persistence.InMemory` | 内存持久化 |
| `Catga.Persistence.Redis` | Redis 持久化 |
| `Catga.Persistence.Nats` | NATS 持久化 |
| `Catga.AspNetCore` | ASP.NET Core 集成 |
| `Catga.SourceGenerator` | 源代码生成器 |

---

## 🔗 快速链接

- [GitHub](https://github.com/Cricle/Catga)
- [示例项目](../examples/)
- [变更日志](CHANGELOG.md)
- [在线文档](https://cricle.github.io/Catga/)

---

## 🤝 贡献

发现问题或有改进建议？欢迎提 Issue 或 PR！

---

<div align="center">

**Made with ❤️ for .NET developers**

</div>
