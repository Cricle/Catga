# 部署文档索引

这一组文档解决的是“开发完成后怎么稳定上线”。

---

## 先看哪几篇

1. [Native AOT 发布](./native-aot-publishing.md)
2. [Kubernetes 部署](./kubernetes.md)
3. [RabbitMQ 生产接入](./rabbitmq-production.md)
4. [NATS 生产接入](./nats-production.md)
5. [Redis 生产接入](./redis-production.md)
6. [AOT 部署文章](../articles/aot-deployment.md)
7. [序列化 AOT 指南](../aot/serialization-aot-guide.md)

---

## 按目标找

### 我要做 Native AOT

- [native-aot-publishing.md](./native-aot-publishing.md)
- [serialization-aot-guide.md](../aot/serialization-aot-guide.md)
- [aot-deployment.md](../articles/aot-deployment.md)

### 我要做容器 / Kubernetes

- [kubernetes.md](./kubernetes.md)

### 我要接标准 broker 上生产

- [rabbitmq-production.md](./rabbitmq-production.md)
- [nats-production.md](./nats-production.md)
- [redis-production.md](./redis-production.md)

### 我要同时看传输和持久化怎么组合

- [rabbitmq-production.md](./rabbitmq-production.md)
- [nats-production.md](./nats-production.md)
- [redis-production.md](./redis-production.md)
- [configuration.md](../articles/configuration.md)

### 我要一起看监控和部署

- [可观测性索引](../observability/README.md)
- [Monitoring Guide](../production/MONITORING-GUIDE.md)

---

## 建议

- 先确认开发期配置，再看部署文档
- 先确认序列化和 AOT 兼容，再做 Native AOT 发布
- 选 broker 时，先确认你要的是“只做传输”还是“传输 + 持久化一体”
- 真正生产落地时，把部署、监控、恢复策略一起看，不要只看单篇部署文档
