# 📁 Catga 解决方案结构

**更新时间**: 2024-10-06  
**解决方案**: Catga.sln  
**项目总数**: 8个

---

## 🏗️ 项目结构树

```
Catga.sln
│
├── 📦 src/ (核心库 - 6个项目)
│   ├── Catga/                                    # 核心框架
│   ├── Catga.Nats/                              # NATS 集成
│   ├── Catga.Redis/                             # Redis 集成
│   ├── Catga.Serialization.Json/                # JSON 序列化器
│   ├── Catga.Serialization.MemoryPack/          # MemoryPack 序列化器
│   └── Catga.ServiceDiscovery.Kubernetes/       # K8s 服务发现
│
├── 🧪 tests/ (测试项目 - 1个)
│   └── Catga.Tests/                             # 单元测试
│
└── 📊 benchmarks/ (性能测试 - 1个)
    └── Catga.Benchmarks/                        # 性能基准测试
```

---

## 📦 核心项目详情

### **1. Catga** (核心框架)
```
路径: src/Catga/Catga.csproj
目标: net9.0
类型: 类库

功能:
- CQRS/Mediator 核心实现
- Pipeline Behaviors
- Result<T> 模式
- Saga 分布式事务
- 健康检查
- 可观测性基础
- 对象池化
```

### **2. Catga.Nats** (NATS 集成)
```
路径: src/Catga.Nats/Catga.Nats.csproj
目标: net9.0
依赖: Catga, NATS.Client.JetStream

功能:
- NATS 分布式消息
- NatsCatgaMediator 实现
- Outbox Store (JetStream)
- Inbox Store (JetStream)
- Idempotency Store (JetStream)
- Request/Event Subscriber
```

### **3. Catga.Redis** (Redis 集成)
```
路径: src/Catga.Redis/Catga.Redis.csproj
目标: net9.0
依赖: Catga, StackExchange.Redis

功能:
- Redis 分布式存储
- Outbox Store
- Inbox Store
- Idempotency Store
- 分布式锁
- Lua 脚本优化
```

### **4. Catga.Serialization.Json** (JSON 序列化器)
```
路径: src/Catga.Serialization.Json/Catga.Serialization.Json.csproj
目标: net9.0
依赖: Catga

功能:
- IMessageSerializer 实现
- System.Text.Json 集成
- AOT 友好
- 源生成器支持
```

### **5. Catga.Serialization.MemoryPack** (MemoryPack 序列化器)
```
路径: src/Catga.Serialization.MemoryPack/Catga.Serialization.MemoryPack.csproj
目标: net9.0
依赖: Catga, MemoryPack

功能:
- IMessageSerializer 实现
- 高性能二进制序列化
- AOT 优化
- 极低内存占用
```

### **6. Catga.ServiceDiscovery.Kubernetes** (K8s 服务发现)
```
路径: src/Catga.ServiceDiscovery.Kubernetes/Catga.ServiceDiscovery.Kubernetes.csproj
目标: net9.0
依赖: Catga, KubernetesClient

功能:
- Kubernetes 原生服务发现
- Service/Endpoints 监听
- 自动服务注册
- 健康检查集成
```

---

## 🧪 测试项目

### **7. Catga.Tests** (单元测试)
```
路径: tests/Catga.Tests/Catga.Tests.csproj
目标: net9.0
测试框架: xUnit
Mock 框架: NSubstitute

覆盖范围:
- CatgaMediator 测试
- Pipeline Behaviors 测试
- Result<T> 模式测试
- Saga 事务测试
- 各种 Store 测试
```

---

## 📊 性能测试项目

### **8. Catga.Benchmarks** (性能基准测试)
```
路径: benchmarks/Catga.Benchmarks/Catga.Benchmarks.csproj
目标: net9.0
框架: BenchmarkDotNet

测试项:
- CQRS 操作性能
- Pipeline 执行性能
- 序列化性能
- 批处理性能
- 流式处理性能
```

---

## 📋 项目依赖关系

```
依赖树:
├── Catga (核心 - 无依赖)
    ├── Catga.Nats → NATS.Client.JetStream
    ├── Catga.Redis → StackExchange.Redis
    ├── Catga.Serialization.Json → (无外部依赖)
    ├── Catga.Serialization.MemoryPack → MemoryPack
    ├── Catga.ServiceDiscovery.Kubernetes → KubernetesClient
    ├── Catga.Tests → xUnit, NSubstitute
    └── Catga.Benchmarks → BenchmarkDotNet
```

---

## 🎯 NuGet 包结构

### **发布的包**
```
1. Catga                                    # 核心框架
2. Catga.Nats                              # NATS 集成
3. Catga.Redis                             # Redis 集成
4. Catga.Serialization.Json                # JSON 序列化器
5. Catga.Serialization.MemoryPack          # MemoryPack 序列化器
6. Catga.ServiceDiscovery.Kubernetes       # K8s 服务发现
```

### **安装示例**
```bash
# 核心框架
dotnet add package Catga

# NATS 集成
dotnet add package Catga.Nats

# Redis 集成
dotnet add package Catga.Redis

# 序列化器（选择一个）
dotnet add package Catga.Serialization.Json
# 或
dotnet add package Catga.Serialization.MemoryPack

# K8s 服务发现（可选）
dotnet add package Catga.ServiceDiscovery.Kubernetes
```

---

## 🔧 开发工具链

### **必需**
- .NET 9 SDK
- C# 13

### **推荐**
- Visual Studio 2022 17.12+
- Rider 2024.3+
- VS Code + C# DevKit

---

## 📊 编译统计

```
编译时间: ~10秒 (Release)
AOT 警告: 79个（已管理）
编译结果: ✅ 成功

项目大小:
- Catga:                           ~500KB
- Catga.Nats:                     ~150KB
- Catga.Redis:                    ~100KB
- Catga.Serialization.Json:        ~30KB
- Catga.Serialization.MemoryPack:  ~30KB
- Catga.ServiceDiscovery.K8s:      ~50KB
```

---

## 🏷️ 版本信息

```
.NET 版本:     9.0
C# 版本:       13
框架目标:      net9.0
可空引用:      启用
AOT 支持:      100%（核心）
```

---

## 📝 添加新项目到解决方案

### **步骤**
```bash
# 1. 创建项目
dotnet new classlib -n Catga.NewProject -o src/Catga.NewProject

# 2. 添加到解决方案
dotnet sln Catga.sln add src/Catga.NewProject/Catga.NewProject.csproj

# 3. 添加项目引用（如需要）
dotnet add src/Catga.NewProject reference src/Catga

# 4. 验证
dotnet build Catga.sln
```

---

## ✅ 项目验证清单

- [x] 所有项目成功编译
- [x] 项目引用正确
- [x] NuGet 依赖完整
- [x] 单元测试通过（Debug模式）
- [x] 性能测试可运行
- [x] AOT 警告已管理
- [x] 文档完整

---

## 🔗 相关文档

- [快速开始](GETTING_STARTED.md)
- [项目概览](PROJECT_OVERVIEW.md)
- [架构设计](ARCHITECTURE.md)
- [API 文档](DOCUMENTATION_INDEX.md)

---

*最后更新: 2024-10-06*

