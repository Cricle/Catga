# Catga Examples

> **30 seconds to start, production-ready distributed system**

[Documentation](https://cricle.github.io/Catga/) · [Architecture](../docs/architecture/ARCHITECTURE.md) · [API Reference](../docs/api/index.md)

---

## 🚀 Quick Start

```powershell
cd examples

# Single instance (simplest)
.\run-demo.ps1 -Mode Single

# Cluster mode (3 replicas + Redis + NATS)
.\run-demo.ps1 -Mode Cluster

# Run tests
.\test-demo.ps1 -StressTest
```

---

## 📊 Performance Benchmarks

Cross-mode stress test results on Windows 11, .NET 9, 16-core CPU:

### Throughput Comparison

| Mode | Infrastructure | Sequential RPS | Parallel RPS | Order RPS | Avg Latency |
|------|----------------|----------------|--------------|-----------|-------------|
| **Single** | In-Memory | 476 req/s | 102 req/s | 33 req/s | 1.94 ms |
| **Aspire (1x)** | Redis + NATS | 239 req/s | 92 req/s | 32 req/s | 4.07 ms |
| **Cluster (3x)** | Redis + NATS | 171 req/s | 94 req/s | 30 req/s | 5.79 ms |

### Latency Distribution

| Mode | Min | Avg | Max | P99 |
|------|-----|-----|-----|-----|
| **Single** | 1.29 ms | 1.94 ms | 22.17 ms | ~20 ms |
| **Aspire (1x)** | 2.22 ms | 4.07 ms | 17.67 ms | ~15 ms |
| **Cluster (3x)** | 1.56 ms | 5.79 ms | 180.36 ms | ~50 ms |

### Infrastructure Status

| Mode | Health | Redis | NATS | Success Rate |
|------|--------|-------|------|--------------|
| **Single** | ✅ OK | N/A | N/A | 100% |
| **Aspire (1x)** | ✅ OK | ✅ 21ms | ✅ OK | 100% |
| **Cluster (3x)** | ✅ OK | ✅ 28ms | ✅ OK | 100% |

> Run `.\cross-test.ps1` to reproduce these benchmarks on your machine.

---

## 🧪 Test Scripts

| Script | Description | Usage |
|--------|-------------|-------|
| `run-demo.ps1` | Start OrderSystem in different modes | `-Mode Single\|Aspire\|Cluster` |
| `test-demo.ps1` | Functional and stress tests | `-TestCluster -StressTest` |
| `cross-test.ps1` | Cross-mode performance comparison | Runs all modes automatically |

### Examples

```powershell
# Functional tests only
.\test-demo.ps1

# Cluster tests with stress
.\test-demo.ps1 -TestCluster -StressTest

# Full cross-mode benchmark
.\cross-test.ps1
```

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Aspire Dashboard (:15888)                │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │
│  │ OrderAPI-1  │  │ OrderAPI-2  │  │ OrderAPI-3  │         │
│  │   (:5275)   │  │   (replica) │  │   (replica) │         │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘         │
│         │                │                │                 │
│         └────────────────┼────────────────┘                 │
│                          │                                  │
│  ┌───────────────────────┴───────────────────────┐         │
│  │                Load Balancer                   │         │
│  └───────────────────────┬───────────────────────┘         │
│                          │                                  │
│  ┌───────────┐    ┌──────┴──────┐    ┌───────────┐         │
│  │   Redis   │    │    NATS     │    │  Jaeger   │         │
│  │  (:6379)  │    │   (:4222)   │    │ (:16686)  │         │
│  └───────────┘    └─────────────┘    └───────────┘         │
└─────────────────────────────────────────────────────────────┘
```

### Components

| Component | Role | Port |
|-----------|------|------|
| **OrderSystem.Api** | Business logic, CQRS handlers | 5275 |
| **Redis** | Distributed cache, order storage | 6379 |
| **NATS** | Message queue, event streaming | 4222 |
| **Jaeger** | Distributed tracing | 16686 |
| **Aspire Dashboard** | Monitoring, logs, metrics | 15888 |

---

## 📁 Project Structure

```
examples/
├── OrderSystem.Api/          # Main API application
│   ├── Handlers/             # CQRS command/query handlers
│   ├── Services/             # Business services
│   ├── Domain/               # Domain models
│   └── wwwroot/              # Web UI
├── OrderSystem.AppHost/      # Aspire orchestration
├── run-demo.ps1              # Start script
├── test-demo.ps1             # Test script
└── cross-test.ps1            # Benchmark script
```

---

## 🔧 Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `Catga__ClusterEnabled` | Enable cluster mode | `false` |
| `Catga__NodeId` | Node identifier | `node-{PID}` |
| `CLUSTER_MODE` | Aspire replica count (true=3) | `false` |

### Aspire Configuration

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis").WithDataVolume();
var nats = builder.AddNats("nats").WithJetStream();

builder.AddProject<Projects.OrderSystem_Api>("order-api")
    .WithReference(redis)
    .WithReference(nats)
    .WithReplicas(3);  // Cluster mode

builder.Build().Run();
```

---

## 🌐 URLs

| Service | URL |
|---------|-----|
| **OrderSystem UI** | http://localhost:5275 |
| **Swagger API** | http://localhost:5275/swagger |
| **Aspire Dashboard** | http://localhost:15888 |
| **Jaeger Tracing** | http://localhost:16686 |
| **Redis Commander** | http://localhost:8081 |

---

## 📚 Related Documentation

- [Getting Started](../docs/articles/getting-started.md)
- [Architecture](../docs/architecture/ARCHITECTURE.md)
- [Distributed Tracing](../docs/observability/DISTRIBUTED-TRACING-GUIDE.md)
- [E2E Scenarios](../docs/examples/e2e-scenarios.md)

---

<div align="center">

**⭐ Production-ready distributed system in 30 seconds!**

</div>
