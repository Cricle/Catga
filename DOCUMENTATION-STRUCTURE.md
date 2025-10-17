# Catga 文档结构

> 最后更新：2025-10-17
> 
> 本文档提供 Catga 框架的完整文档索引和结构说明。

---

## 📂 文档组织结构

```
Catga/
├── README.md                          # 项目主页（快速开始）
├── CONTRIBUTING.md                    # 贡献指南
├── docs/
│   ├── INDEX.md                       # 文档总索引
│   ├── QUICK-START.md                 # 快速开始（详细版）
│   ├── QUICK-REFERENCE.md             # API 速查表
│   ├── CHANGELOG.md                   # 变更日志
│   ├── FRAMEWORK-ROADMAP.md           # 框架路线图
│   ├── PROJECT_STRUCTURE.md           # 项目结构说明
│   ├── PROJECT_SUMMARY.md             # 项目总结
│   ├── RELEASE-READINESS-CHECKLIST.md # 发布检查清单
│   ├── PERFORMANCE-REPORT.md          # 性能报告
│   ├── BENCHMARK-RESULTS.md           # 基准测试结果
│   │
│   ├── api/                           # API 参考文档
│   │   ├── README.md                  # API 文档索引
│   │   ├── mediator.md                # ICatgaMediator API
│   │   └── messages.md                # 消息契约 API
│   │
│   ├── guides/                        # 使用指南
│   │   ├── auto-di-registration.md   # 自动依赖注入
│   │   ├── custom-error-handling.md  # 自定义错误处理
│   │   ├── serialization.md          # 序列化配置
│   │   ├── source-generator.md       # Source Generator 使用
│   │   ├── source-generator-usage.md # Source Generator 高级用法
│   │   ├── distributed-id.md         # 分布式 ID 生成
│   │   └── analyzers.md              # Roslyn 分析器
│   │
│   ├── architecture/                 # 架构设计
│   │   ├── overview.md               # 架构概览
│   │   ├── cqrs.md                   # CQRS 模式
│   │   ├── ARCHITECTURE.md           # 详细架构设计
│   │   └── RESPONSIBILITY-BOUNDARY.md# 职责边界
│   │
│   ├── patterns/                     # 设计模式
│   │   └── DISTRIBUTED-TRANSACTION-V2.md # 分布式事务（Catga Pattern）
│   │
│   ├── observability/                # 可观测性
│   │   ├── DISTRIBUTED-TRACING-GUIDE.md # 分布式追踪指南
│   │   └── JAEGER-COMPLETE-GUIDE.md     # Jaeger 完整指南
│   │
│   ├── aot/                          # AOT 兼容性
│   │   └── serialization-aot-guide.md   # AOT 序列化指南
│   │
│   ├── deployment/                   # 部署文档
│   │   ├── native-aot-publishing.md      # Native AOT 发布
│   │   └── kubernetes.md                 # Kubernetes 部署
│   │
│   ├── production/                   # 生产环境
│   │   └── MONITORING-GUIDE.md           # 监控指南（Prometheus/Grafana）
│   │
│   ├── distributed/                  # 分布式系统
│   │   ├── README.md                     # 分布式系统总览
│   │   ├── ARCHITECTURE.md               # 分布式架构
│   │   └── KUBERNETES.md                 # Kubernetes 集成
│   │
│   ├── examples/                     # 示例文档
│   │   └── basic-usage.md                # 基础用法示例
│   │
│   └── analyzers/                    # 分析器文档
│       └── README.md                     # Roslyn 分析器说明
│
├── examples/                         # 完整示例项目
│   ├── OrderSystem.Api/              # 订单系统 API
│   │   └── README.md                     # 订单系统文档
│   ├── OrderSystem.AppHost/          # Aspire AppHost
│   │   ├── README.md                     # AppHost 说明
│   │   └── README-GRACEFUL.md            # 优雅关闭说明
│   └── README.md                         # 示例总览
│
├── src/                              # 源代码
│   ├── Catga/                        # 核心框架
│   │   └── README.md                     # 核心框架说明
│   ├── Catga.AspNetCore/             # ASP.NET Core 集成
│   │   └── README.md                     # ASP.NET Core 集成说明
│   └── Catga.SourceGenerator/        # 源生成器
│       ├── README.md                     # 源生成器说明
│       └── Analyzers/README.md           # 分析器说明
│
└── benchmarks/                       # 性能基准测试
    └── Catga.Benchmarks/
        └── README.md                     # 基准测试说明
```

---

## 📚 文档分类

### 🚀 入门文档

适合新用户快速上手：

1. **[README.md](../README.md)** - 项目主页
   - 30 秒示例
   - 核心特性概览
   - 快速安装指南

2. **[docs/QUICK-START.md](./QUICK-START.md)** - 详细快速开始
   - 完整的项目设置
   - 一步步教程
   - 常见问题

3. **[docs/QUICK-REFERENCE.md](./QUICK-REFERENCE.md)** - API 速查表
   - 常用 API 一览
   - 代码片段
   - 快速参考

### 📖 核心文档

理解框架的核心概念：

1. **[docs/api/messages.md](./api/messages.md)** - 消息契约
   - IRequest / IEvent 接口
   - 消息定义规范
   - MemoryPack 序列化

2. **[docs/api/mediator.md](./api/mediator.md)** - Mediator API
   - ICatgaMediator 接口
   - SendAsync / PublishAsync
   - 批量操作

3. **[docs/guides/custom-error-handling.md](./guides/custom-error-handling.md)** - 错误处理
   - SafeRequestHandler 使用
   - 自定义错误处理
   - 自动回滚实现

4. **[docs/guides/source-generator.md](./guides/source-generator.md)** - Source Generator
   - 自动注册原理
   - AddGeneratedHandlers()
   - AddGeneratedServices()

### 🏗️ 架构文档

深入理解框架设计：

1. **[docs/architecture/overview.md](./architecture/overview.md)** - 架构概览
2. **[docs/architecture/cqrs.md](./architecture/cqrs.md)** - CQRS 模式
3. **[docs/architecture/ARCHITECTURE.md](./architecture/ARCHITECTURE.md)** - 详细架构
4. **[docs/patterns/DISTRIBUTED-TRANSACTION-V2.md](./patterns/DISTRIBUTED-TRANSACTION-V2.md)** - 分布式事务

### 🔍 可观测性文档

监控、追踪、调试：

1. **[docs/observability/DISTRIBUTED-TRACING-GUIDE.md](./observability/DISTRIBUTED-TRACING-GUIDE.md)**
   - 分布式追踪原理
   - 跨服务链路传播
   - OpenTelemetry 配置

2. **[docs/observability/JAEGER-COMPLETE-GUIDE.md](./observability/JAEGER-COMPLETE-GUIDE.md)**
   - Jaeger 安装配置
   - 搜索技巧
   - 与 Grafana 集成

3. **[docs/production/MONITORING-GUIDE.md](./production/MONITORING-GUIDE.md)**
   - Prometheus 集成
   - Grafana 仪表板
   - 关键指标

### 🚀 部署文档

生产环境部署：

1. **[docs/deployment/native-aot-publishing.md](./deployment/native-aot-publishing.md)**
   - Native AOT 配置
   - 发布步骤
   - 优化技巧

2. **[docs/deployment/kubernetes.md](./deployment/kubernetes.md)**
   - Kubernetes 部署
   - Helm Charts
   - 最佳实践

3. **[docs/aot/serialization-aot-guide.md](./aot/serialization-aot-guide.md)**
   - AOT 序列化指南
   - MemoryPack 配置
   - 常见问题

### 📊 性能文档

性能基准和优化：

1. **[docs/PERFORMANCE-REPORT.md](./PERFORMANCE-REPORT.md)**
   - 性能对比报告
   - 与其他框架对比
   - 优化建议

2. **[docs/BENCHMARK-RESULTS.md](./BENCHMARK-RESULTS.md)**
   - 详细基准测试结果
   - 吞吐量/延迟数据
   - 内存分配分析

### 🎨 示例文档

完整的可运行示例：

1. **[examples/OrderSystem.Api/README.md](../examples/OrderSystem.Api/README.md)**
   - 订单系统完整示例
   - 成功/失败流程
   - 自动回滚演示

2. **[examples/OrderSystem.AppHost/README.md](../examples/OrderSystem.AppHost/README.md)**
   - .NET Aspire 集成
   - 服务编排
   - 健康检查

---

## 🔗 外部资源

### .NET 官方文档
- [.NET 9 文档](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [Native AOT 指南](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [Source Generators](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/source-generators-overview)

### 可观测性工具
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/net/)
- [Jaeger 文档](https://www.jaegertracing.io/docs/)
- [Prometheus 文档](https://prometheus.io/docs/)
- [Grafana 文档](https://grafana.com/docs/)

### 相关项目
- [MediatR](https://github.com/jbogard/MediatR)
- [MassTransit](https://github.com/MassTransit/MassTransit)
- [MemoryPack](https://github.com/Cysharp/MemoryPack)
- [NATS](https://nats.io/)

---

## 📋 文档维护指南

### 添加新文档

1. **选择合适的目录**
   - API 文档 → `docs/api/`
   - 使用指南 → `docs/guides/`
   - 架构设计 → `docs/architecture/`
   - 部署相关 → `docs/deployment/`

2. **命名规范**
   - 使用 kebab-case: `my-new-feature.md`
   - 使用描述性名称
   - 避免缩写

3. **文档结构**
   - 标题层级清晰
   - 包含代码示例
   - 添加交叉引用

4. **更新索引**
   - 更新 `docs/INDEX.md`
   - 更新本文档 (`DOCUMENTATION-STRUCTURE.md`)
   - 更新 `README.md` （如果需要）

### 更新现有文档

1. **检查准确性** - 确保代码示例可运行
2. **更新版本信息** - 更新"最后更新"日期
3. **检查链接** - 确保所有链接有效
4. **运行拼写检查** - 避免拼写错误

### 文档审查清单

- [ ] 代码示例可编译
- [ ] 链接全部有效
- [ ] 标题层级正确
- [ ] 格式一致
- [ ] 无拼写错误
- [ ] 更新日期正确

---

## 🤝 贡献文档

欢迎贡献文档！请遵循以下步骤：

1. Fork 仓库
2. 创建文档分支：`git checkout -b docs/my-improvement`
3. 编写/更新文档
4. 提交变更：`git commit -m "docs: 改进 XXX 文档"`
5. 推送分支：`git push origin docs/my-improvement`
6. 创建 Pull Request

详见：[CONTRIBUTING.md](../CONTRIBUTING.md)

---

<div align="center">

**📚 文档持续更新中**

如有任何问题或建议，欢迎[提交 Issue](https://github.com/your-org/Catga/issues)

</div>

