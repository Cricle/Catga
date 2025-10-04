# Catga 文档

欢迎来到 Catga 框架文档！

## 📚 文档导航

### 快速开始
- [安装指南](guides/installation.md)
- [快速开始](guides/quick-start.md)
- [配置选项](guides/configuration.md)

### 核心概念
- [架构概览](architecture/overview.md)
- [CQRS 模式](architecture/cqrs.md)
- [CatGa 分布式事务](architecture/catga-transactions.md)
- [Pipeline 行为](architecture/pipeline-behaviors.md)

### API 参考
- [ICatgaMediator](api/mediator.md)
- [消息类型](api/messages.md)
- [处理器](api/handlers.md)
- [结果类型](api/results.md)
- [异常处理](api/exceptions.md)

### 指南
- [发送命令](guides/commands.md)
- [执行查询](guides/queries.md)
- [发布事件](guides/events.md)
- [分布式事务](guides/distributed-transactions.md)
- [幂等性](guides/idempotency.md)
- [弹性机制](guides/resilience.md)

### 扩展
- [NATS 传输](guides/nats-transport.md)
- [Redis 持久化](guides/redis-persistence.md)
- [自定义 Pipeline Behavior](guides/custom-behaviors.md)

### 示例
- [简单 CQRS 应用](examples/simple-cqrs.md)
- [微服务架构](examples/microservices.md)
- [事件驱动系统](examples/event-driven.md)

### 高级主题
- [性能优化](guides/performance.md)
- [可观测性](guides/observability.md)
- [AOT 部署](guides/aot-deployment.md)
- [最佳实践](guides/best-practices.md)

## 🎯 推荐阅读路径

### 初学者
1. [快速开始](guides/quick-start.md)
2. [CQRS 模式](architecture/cqrs.md)
3. [发送命令](guides/commands.md)
4. [执行查询](guides/queries.md)

### 进阶用户
1. [架构概览](architecture/overview.md)
2. [Pipeline 行为](architecture/pipeline-behaviors.md)
3. [弹性机制](guides/resilience.md)
4. [自定义 Behavior](guides/custom-behaviors.md)

### 分布式系统
1. [CatGa 分布式事务](architecture/catga-transactions.md)
2. [NATS 传输](guides/nats-transport.md)
3. [Redis 持久化](guides/redis-persistence.md)
4. [微服务架构示例](examples/microservices.md)

## 🔗 外部资源

- [GitHub 仓库](https://github.com/yourusername/Catga)
- [问题追踪](https://github.com/yourusername/Catga/issues)
- [变更日志](../CHANGELOG.md)
- [贡献指南](../CONTRIBUTING.md)

## 📝 文档贡献

发现文档错误或想要改进？欢迎提交 PR！

```bash
# 克隆仓库
git clone https://github.com/yourusername/Catga.git

# 编辑文档
cd Catga/docs
# 编辑 Markdown 文件

# 提交 PR
git add .
git commit -m "docs: improve XXX documentation"
git push
```

## 💬 获取帮助

- 💬 [Discussions](https://github.com/yourusername/Catga/discussions)
- 🐛 [报告问题](https://github.com/yourusername/Catga/issues/new)
- 📧 Email: support@catga.dev

---

**Catga** - 高性能、AOT 兼容的 CQRS 和分布式事务框架 🚀

