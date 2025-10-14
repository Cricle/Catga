# Catga 文档中心

欢迎来到 Catga 文档中心！这里包含所有你需要的信息，从快速开始到高级主题。

---

## 🚀 快速开始

- [30 秒快速开始](../README.md#-快速开始) - 最快的入门方式
- [API 速查](../QUICK-REFERENCE.md) - 常用 API 和模式
- [基础使用示例](./examples/basic-usage.md) - 完整的入门示例

---

## 📚 核心概念

### CQRS 和架构

- [CQRS 模式详解](./architecture/cqrs.md) - 命令查询职责分离
- [系统架构](./architecture/ARCHITECTURE.md) - Catga 整体架构
- [架构概览](./architecture/overview.md) - 高层次设计理念
- [职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md) - 模块职责划分

### 消息和 API

- [消息类型](./api/messages.md) - Command, Query, Event 详解
- [Mediator API](./api/mediator.md) - ICatgaMediator 使用指南
- [API 参考](./api/README.md) - 完整 API 文档

---

## 🔧 使用指南

### 序列化

- [序列化指南](./guides/serialization.md) - MemoryPack vs JSON
- [AOT 序列化配置](../docs/aot/serialization-aot-guide.md) - Native AOT 序列化

### Source Generator 和分析器

- [Source Generator 使用](./guides/source-generator.md) - 自动生成代码
- [Roslyn 分析器](./guides/analyzers.md) - 编译时检查
- [分析器详解](./analyzers/README.md) - CATGA001, CATGA002

### 分布式功能

- [分布式 ID](./guides/distributed-id.md) - Snowflake ID 生成器
- [分布式架构](./distributed/ARCHITECTURE.md) - 分布式系统设计
- [Kubernetes 集成](./distributed/KUBERNETES.md) - K8s 服务发现

---

## 🚢 部署

### Native AOT

- [Native AOT 发布](./deployment/native-aot-publishing.md) - AOT 编译和发布
- [AOT 序列化指南](./aot/serialization-aot-guide.md) - 序列化器 AOT 配置

### Kubernetes

- [Kubernetes 部署](./deployment/kubernetes.md) - K8s 部署完整指南

---

## 🏗️ 架构和模式

### 分布式模式

- [分布式事务 V2](./patterns/DISTRIBUTED-TRANSACTION-V2.md) - Catga 独特的分布式事务方案

---

## 📖 示例项目

我们提供了完整的示例项目：

### OrderSystem (完整示例)

完整的订单系统，演示 Catga 的所有核心功能：

- **位置**: [examples/OrderSystem.AppHost/](../examples/OrderSystem.AppHost/)
- **功能**:
  - .NET Aspire 编排
  - CQRS 命令和查询
  - 事件发布和订阅
  - NATS 消息传输
  - Redis 持久化
  - ASP.NET Core 集成
  - 分布式 ID 生成
  - 幂等性保证

**快速运行**:
```bash
cd examples/OrderSystem.AppHost
dotnet run
```

### MemoryPackAotDemo (AOT 示例)

最小化的 Native AOT 示例：

- **位置**: [examples/MemoryPackAotDemo/](../examples/MemoryPackAotDemo/)
- **功能**:
  - 100% AOT 兼容
  - MemoryPack 序列化
  - 最小化二进制 (< 10MB)
  - 快速启动 (< 50ms)

**编译为 AOT**:
```bash
cd examples/MemoryPackAotDemo
dotnet publish -c Release -r linux-x64 --property:PublishAot=true
```

---

## 🎯 按使用场景导航

### 新手入门

1. [30 秒快速开始](../README.md#-快速开始)
2. [基础使用示例](./examples/basic-usage.md)
3. [API 速查](../QUICK-REFERENCE.md)
4. [CQRS 模式详解](./architecture/cqrs.md)

### 开发生产应用

1. [系统架构](./architecture/ARCHITECTURE.md)
2. [序列化指南](./guides/serialization.md)
3. [分布式 ID](./guides/distributed-id.md)
4. [Roslyn 分析器](./guides/analyzers.md)

### Native AOT 部署

1. [Native AOT 发布](./deployment/native-aot-publishing.md)
2. [AOT 序列化配置](./aot/serialization-aot-guide.md)
3. [MemoryPackAotDemo 示例](../examples/MemoryPackAotDemo/)

### Kubernetes 部署

1. [Kubernetes 部署指南](./deployment/kubernetes.md)
2. [分布式架构](./distributed/ARCHITECTURE.md)
3. [K8s 集成](./distributed/KUBERNETES.md)

### 性能优化

1. [性能基准测试](../benchmarks/README.md)
2. [架构概览](./architecture/overview.md)
3. [分布式 ID](./guides/distributed-id.md)

---

## 📊 文档结构

```
docs/
├── README.md                        # 本文档 (导航)
│
├── api/                             # API 参考
│   ├── README.md                    # API 文档首页
│   ├── mediator.md                  # Mediator API
│   └── messages.md                  # 消息类型
│
├── architecture/                    # 架构设计
│   ├── ARCHITECTURE.md              # 系统架构
│   ├── cqrs.md                      # CQRS 模式
│   ├── overview.md                  # 架构概览
│   └── RESPONSIBILITY-BOUNDARY.md   # 职责边界
│
├── guides/                          # 使用指南
│   ├── serialization.md             # 序列化指南
│   ├── source-generator.md          # Source Generator
│   ├── analyzers.md                 # Roslyn 分析器
│   └── distributed-id.md            # 分布式 ID
│
├── deployment/                      # 部署指南
│   ├── native-aot-publishing.md     # AOT 发布
│   └── kubernetes.md                # Kubernetes 部署
│
├── distributed/                     # 分布式功能
│   ├── ARCHITECTURE.md              # 分布式架构
│   ├── KUBERNETES.md                # K8s 集成
│   └── README.md                    # 分布式功能概览
│
├── patterns/                        # 设计模式
│   └── DISTRIBUTED-TRANSACTION-V2.md # 分布式事务
│
├── aot/                             # AOT 相关
│   └── serialization-aot-guide.md   # AOT 序列化
│
├── analyzers/                       # 分析器文档
│   └── README.md                    # 分析器详解
│
└── examples/                        # 示例文档
    └── basic-usage.md               # 基础使用
```

---

## 🤝 贡献文档

发现文档错误或有改进建议？

1. Fork 项目
2. 编辑文档
3. 提交 Pull Request

或者直接在 [GitHub Issues](https://github.com/Cricle/Catga/issues) 中反馈。

---

## 📝 文档更新

- **最后更新**: 2025-10-14
- **版本**: v1.0.0
- **语言**: 简体中文

---

## 🔗 相关链接

- [项目主页](../README.md)
- [API 速查](../QUICK-REFERENCE.md)
- [更新日志](../CHANGELOG.md)
- [发布就绪检查](../RELEASE-READINESS-CHECKLIST.md)
- [测试覆盖总结](../TEST-COVERAGE-SUMMARY.md)
- [最终发布总结](../FINAL-RELEASE-SUMMARY.md)

---

<div align="center">

**📖 Happy Coding with Catga!**

[GitHub](https://github.com/Cricle/Catga) · [NuGet](https://www.nuget.org/packages/Catga/) · [示例](../examples/)

</div>
