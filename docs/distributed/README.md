# 🌐 分布式与集群架构

本目录包含 Catga 框架的分布式和集群架构文档。

---

## 📚 文档列表

### 🎯 核心架构文档

#### [CLUSTER_ARCHITECTURE_ANALYSIS.md](./CLUSTER_ARCHITECTURE_ANALYSIS.md) ⭐ 推荐
**集群架构全面分析**

- 📊 集群架构概览
- 🎯 无主多节点 (P2P) 架构 - 原生支持
- 🔸 1主多从 (Master-Slave) 架构 - 可实现
- 🔷 推荐架构组合
- 📈 性能对比分析
- 🚀 快速启动示例

**适合**: 了解 Catga 集群能力和架构选择

---

#### [PEER_TO_PEER_ARCHITECTURE.md](./PEER_TO_PEER_ARCHITECTURE.md)
**无主多从（Peer-to-Peer）架构详解**

- 🔄 P2P 架构设计原理
- ✅ NATS 队列组负载均衡
- 🛡️ 无单点故障特性
- 📊 性能测试数据
- 🛠️ 实现代码示例

**适合**: 深入理解 P2P 架构实现

---

#### [DISTRIBUTED_CLUSTER_SUPPORT.md](./DISTRIBUTED_CLUSTER_SUPPORT.md)
**分布式与集群支持完整指南**

- 🌐 分布式能力全景
- 🏗️ 集群部署架构
- 🔄 负载均衡策略
- 🛡️ 高可用保证
- 📊 性能与扩展性
- 🔧 完整配置示例
- 🎯 最佳实践

**适合**: 生产环境部署参考

---

## 🎯 快速导航

### 我应该先读哪个？

**如果你想了解集群能力** → 从 [CLUSTER_ARCHITECTURE_ANALYSIS.md](./CLUSTER_ARCHITECTURE_ANALYSIS.md) 开始

**如果你想深入 P2P 架构** → 阅读 [PEER_TO_PEER_ARCHITECTURE.md](./PEER_TO_PEER_ARCHITECTURE.md)

**如果你要部署到生产环境** → 参考 [DISTRIBUTED_CLUSTER_SUPPORT.md](./DISTRIBUTED_CLUSTER_SUPPORT.md)

---

## 🔑 核心概念

### 无主多节点 (P2P) ⭐ 推荐

```
所有服务实例对等，无主节点
NATS 自动负载均衡
无单点故障
故障转移 < 1秒
```

**优势**:
- ✅ 最简单 - 零配置
- ✅ 最高可用 - 无单点故障
- ✅ 最易扩展 - 添加节点即时生效

### 1主多从 (Master-Slave) 可选

```
通过 Redis 分布式锁实现
适用于需要协调的场景
Saga 协调器、定时任务
```

**优势**:
- ✅ 强一致性
- ✅ 适合协调场景
- ⚠️ 主节点可能成为瓶颈

---

## 📊 架构对比

| 维度 | P2P (无主) | Master-Slave (主从) |
|------|-----------|---------------------|
| **复杂度** | ⭐⭐ 简单 | ⭐⭐⭐⭐ 复杂 |
| **性能** | ⭐⭐⭐⭐⭐ 高 | ⭐⭐⭐ 中等 |
| **可用性** | ⭐⭐⭐⭐⭐ 最高 | ⭐⭐⭐⭐ 高 |
| **扩展性** | ⭐⭐⭐⭐⭐ 线性 | ⭐⭐⭐ 受限 |

---

## 🚀 快速开始

### 启动 P2P 集群

```bash
# 1. 启动 NATS 集群
docker-compose up -d nats-cluster

# 2. 启动 Redis 集群
docker-compose up -d redis-cluster

# 3. 启动服务实例（P2P 模式）
docker-compose up -d --scale order-service=5
```

### 配置代码

```csharp
// P2P 架构配置（推荐）
services.AddCatga()
    .AddNatsCatga("nats://node1,node2,node3")
    .AddRedisCatgaStore("redis://cluster");

// 部署：每个服务 3-5 个对等实例
```

---

## 📈 性能数据

### 吞吐量（P2P 架构）

| 实例数 | 总吞吐量 | 扩展效率 |
|--------|---------|---------|
| 1      | 10,000  | 100%    |
| 3      | 27,000  | 90%     |
| 10     | 82,000  | 82%     |

### 故障恢复时间

| 场景 | 恢复时间 | 影响 |
|-----|---------|------|
| 单实例故障 | < 1秒 | 0% |
| NATS 节点故障 | < 1秒 | 0% |
| Redis 节点故障 | < 5秒 | < 1% |

---

## 🎉 总结

**Catga 完全支持分布式和集群部署！**

✅ **推荐使用 P2P 架构** - 简单、高可用、易扩展
💡 **特定场景可用主从** - Saga 协调、定时任务
🚀 **生产就绪** - 99.9%+ 可用性

---

**返回**: [文档首页](../README.md)

