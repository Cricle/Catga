# 📚 Catga 文档索引

欢迎来到 Catga 框架文档中心！本页面提供所有文档的快速导航。

---

## 🚀 快速入门

### 新手必读

| 文档 | 说明 | 预计时间 |
|------|------|---------|
| [README.md](README.md) | 项目概览和核心特性 | 5 分钟 |
| [QUICK_START.md](QUICK_START.md) | 5分钟快速上手 | 5 分钟 |
| [QUICK_REFERENCE.md](QUICK_REFERENCE.md) | API 速查手册 | 3 分钟 |

---

## 📖 核心文档

### 架构与设计

| 文档 | 说明 |
|------|------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | 功能分层和架构设计 |
| [docs/architecture/overview.md](docs/architecture/overview.md) | 架构概览 |
| [docs/architecture/cqrs.md](docs/architecture/cqrs.md) | CQRS 模式详解 |

### API 文档

| 文档 | 说明 |
|------|------|
| [docs/api/mediator.md](docs/api/mediator.md) | Mediator API 参考 |
| [docs/api/messages.md](docs/api/messages.md) | 消息类型定义 |

---

## 🌐 分布式与集群

### 集群架构 ⭐ 重点

| 文档 | 说明 | 推荐度 |
|------|------|--------|
| [docs/distributed/CLUSTER_ARCHITECTURE_ANALYSIS.md](docs/distributed/CLUSTER_ARCHITECTURE_ANALYSIS.md) | 集群架构全面分析 | ⭐⭐⭐⭐⭐ |
| [docs/distributed/PEER_TO_PEER_ARCHITECTURE.md](docs/distributed/PEER_TO_PEER_ARCHITECTURE.md) | P2P 架构详解 | ⭐⭐⭐⭐ |
| [docs/distributed/DISTRIBUTED_CLUSTER_SUPPORT.md](docs/distributed/DISTRIBUTED_CLUSTER_SUPPORT.md) | 分布式部署完整指南 | ⭐⭐⭐⭐⭐ |
| [docs/distributed/README.md](docs/distributed/README.md) | 分布式文档索引 | - |

**适合场景**:
- 了解 Catga 集群能力
- 生产环境部署
- P2P vs 主从架构选择

---

## 🛡️ 可靠性模式

### Outbox/Inbox 模式

| 文档 | 说明 |
|------|------|
| [docs/patterns/outbox-inbox.md](docs/patterns/outbox-inbox.md) | Outbox/Inbox 模式说明 |
| [docs/patterns/OUTBOX_INBOX_IMPLEMENTATION.md](docs/patterns/OUTBOX_INBOX_IMPLEMENTATION.md) | 实现细节和代码示例 |

**核心价值**:
- ✅ 消息可靠投递 (Outbox)
- ✅ 幂等消息处理 (Inbox)
- ✅ 分布式事务协调 (Saga)

---

## ⚡ 性能优化

### 性能文档 ⭐ 重点

| 文档 | 说明 | 推荐度 |
|------|------|--------|
| [docs/performance/PERFORMANCE_IMPROVEMENTS.md](docs/performance/PERFORMANCE_IMPROVEMENTS.md) | 最新性能优化报告 | ⭐⭐⭐⭐⭐ |
| [docs/performance/AOT_FINAL_REPORT.md](docs/performance/AOT_FINAL_REPORT.md) | Native AOT 优化 | ⭐⭐⭐⭐ |
| [docs/performance/README.md](docs/performance/README.md) | 性能文档索引 | - |
| [benchmarks/PERFORMANCE_BENCHMARK_RESULTS.md](benchmarks/PERFORMANCE_BENCHMARK_RESULTS.md) | 基准测试结果 | ⭐⭐⭐ |

**性能成果**:
- ✅ 吞吐量提升 18.5%
- ✅ 延迟降低 30%
- ✅ 内存减少 33%
- ✅ GC 压力降低 40%

---

## 🎯 Native AOT

### AOT 兼容性

| 文档 | 说明 |
|------|------|
| [docs/aot/README.md](docs/aot/README.md) | AOT 概览 |
| [docs/aot/native-aot-guide.md](docs/aot/native-aot-guide.md) | Native AOT 使用指南 |

**AOT 优势**:
- ✅ 100% AOT 兼容
- ✅ 启动时间减少 50%
- ✅ 内存占用减少 30%
- ✅ 部署包减少 40%

---

## 📊 可观测性

### 监控与追踪

| 文档 | 说明 |
|------|------|
| [docs/observability/README.md](docs/observability/README.md) | 可观测性概览 |
| [docs/observability/OBSERVABILITY_COMPLETE.md](docs/observability/OBSERVABILITY_COMPLETE.md) | 完整可观测性指南 |

**包含内容**:
- 📈 Metrics (Prometheus)
- 🔍 Tracing (OpenTelemetry)
- 📝 Logging (结构化日志)
- 🏥 Health Checks

---

## 📘 使用指南

### 指南文档

| 文档 | 说明 |
|------|------|
| [docs/guides/quick-start.md](docs/guides/quick-start.md) | 详细快速开始指南 |
| [docs/guides/API_TESTING_GUIDE.md](docs/guides/API_TESTING_GUIDE.md) | API 测试指南 |

### 示例代码

| 文档 | 说明 |
|------|------|
| [docs/examples/basic-usage.md](docs/examples/basic-usage.md) | 基础使用示例 |
| [examples/README.md](examples/README.md) | 示例项目说明 |

---

## 🔧 项目管理

### 项目状态

| 文档 | 说明 |
|------|------|
| [PROJECT_STATUS.md](PROJECT_STATUS.md) | 当前项目状态 |
| [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) | 项目结构说明 |

### 贡献指南

| 文档 | 说明 |
|------|------|
| [CONTRIBUTING.md](CONTRIBUTING.md) | 如何贡献代码 |
| [LICENSE](LICENSE) | MIT 许可证 |

---

## 📦 历史文档

### 归档文档

| 文档 | 说明 |
|------|------|
| [docs/archive/LOCK_FREE_OPTIMIZATION.md](docs/archive/LOCK_FREE_OPTIMIZATION.md) | 无锁优化历史记录 |
| [docs/archive/README.md](docs/archive/README.md) | 归档文档索引 |

---

## 🗺️ 学习路径

### 推荐学习顺序

#### 第 1 天: 入门 (30分钟)

1. 阅读 [README.md](README.md)
2. 跟随 [QUICK_START.md](QUICK_START.md)
3. 查看 [基础示例](docs/examples/basic-usage.md)

#### 第 2-3 天: 核心功能 (2小时)

1. 学习 [CQRS 模式](docs/architecture/cqrs.md)
2. 掌握 [Mediator API](docs/api/mediator.md)
3. 理解 [Pipeline Behaviors](docs/guides/quick-start.md)

#### 第 4-5 天: 分布式 (3小时)

1. 理解 [集群架构](docs/distributed/CLUSTER_ARCHITECTURE_ANALYSIS.md)
2. 学习 [P2P 架构](docs/distributed/PEER_TO_PEER_ARCHITECTURE.md)
3. 阅读 [部署指南](docs/distributed/DISTRIBUTED_CLUSTER_SUPPORT.md)

#### 第 2 周: 高级特性 (4小时)

1. 掌握 [Outbox/Inbox](docs/patterns/outbox-inbox.md)
2. 学习 [可观测性](docs/observability/README.md)
3. 理解 [性能优化](docs/performance/README.md)

#### 第 3 周: 生产就绪 (按需)

1. AOT 优化 - [Native AOT 指南](docs/aot/native-aot-guide.md)
2. 性能调优 - [性能报告](docs/performance/PERFORMANCE_IMPROVEMENTS.md)
3. 监控部署 - [可观测性指南](docs/observability/OBSERVABILITY_COMPLETE.md)

---

## 🎯 按场景查找

### 我想...

#### 快速上手
→ [QUICK_START.md](QUICK_START.md)

#### 了解集群能力
→ [集群架构分析](docs/distributed/CLUSTER_ARCHITECTURE_ANALYSIS.md)

#### 部署到生产环境
→ [分布式部署指南](docs/distributed/DISTRIBUTED_CLUSTER_SUPPORT.md)

#### 优化性能
→ [性能优化报告](docs/performance/PERFORMANCE_IMPROVEMENTS.md)

#### 确保消息可靠性
→ [Outbox/Inbox 模式](docs/patterns/outbox-inbox.md)

#### 监控服务健康
→ [可观测性指南](docs/observability/README.md)

#### 使用 Native AOT
→ [Native AOT 指南](docs/aot/native-aot-guide.md)

#### 贡献代码
→ [贡献指南](CONTRIBUTING.md)

---

## 📊 文档统计

### 文档类型分布

| 类型 | 数量 | 说明 |
|------|------|------|
| **核心文档** | 3 | README, QUICK_START, ARCHITECTURE |
| **架构文档** | 2 | overview, cqrs |
| **API 文档** | 2 | mediator, messages |
| **分布式文档** | 3 | 集群架构、P2P、部署指南 |
| **性能文档** | 2 | 性能优化、AOT 报告 |
| **可观测性** | 2 | 监控、追踪 |
| **指南/示例** | 3 | quick-start, testing, basic-usage |
| **项目管理** | 3 | 状态、结构、贡献指南 |

**总计**: ~20 个主要文档

---

## 🔍 快速搜索

### 常见关键词

- **集群/分布式** → [分布式文档](docs/distributed/)
- **性能** → [性能文档](docs/performance/)
- **P2P/无主** → [P2P 架构](docs/distributed/PEER_TO_PEER_ARCHITECTURE.md)
- **Outbox/Inbox** → [可靠性模式](docs/patterns/)
- **AOT** → [AOT 文档](docs/aot/)
- **监控** → [可观测性](docs/observability/)
- **API** → [API 文档](docs/api/)

---

## 💡 文档维护

### 文档状态

| 状态 | 说明 |
|------|------|
| ✅ **最新** | 内容准确，与代码同步 |
| 🔄 **维护中** | 正在更新 |
| 📦 **归档** | 历史参考，可能过时 |

**最后更新**: 2025-10-06

---

## 🤝 文档贡献

发现文档问题或有改进建议？

1. 提交 [Issue](https://github.com/你的用户名/Catga/issues)
2. 提交 [Pull Request](https://github.com/你的用户名/Catga/pulls)
3. 参与 [Discussions](https://github.com/你的用户名/Catga/discussions)

---

## 📞 获取帮助

- 📖 先查阅相关文档
- 💬 在 [Discussions](https://github.com/你的用户名/Catga/discussions) 提问
- 🐛 在 [Issues](https://github.com/你的用户名/Catga/issues) 报告 Bug
- 📧 通过邮件联系维护者

---

**Catga 文档 - 帮助你构建高性能分布式应用！** 📚✨
