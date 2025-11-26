# OrderSystem.AppHost - Aspire Orchestration

## 🎯 Overview

Complete .NET Aspire orchestration for Catga OrderSystem - one command starts everything!

**Features**:
- ✅ Auto-starts Redis + NATS containers
- ✅ 3-replica OrderSystem.Api cluster
- ✅ OpenTelemetry tracing & metrics
- ✅ Health checks & resilience
- ✅ Service discovery
- ✅ Aspire Dashboard monitoring

---

## 🚀 Quick Start

```bash
cd examples/OrderSystem.AppHost
dotnet run
```

**Access**:
- 🎛️ **Aspire Dashboard**: http://localhost:15888
- 🌐 **OrderSystem API**: http://localhost:5000/swagger
- 📊 **Redis Commander**: http://localhost:8081
- 🔍 **Health Check**: http://localhost:5000/health

---

## 🏗️ Infrastructure

### Redis
- **Purpose**: Distributed cache, locks, idempotency
- **Port**: 6379
- **Commander UI**: http://localhost:8081
- **Data Volume**: Persistent

### NATS
- **Purpose**: Distributed messaging
- **Port**: 4222
- **JetStream**: Enabled
- **Data Volume**: Persistent

---

## 🎯 OrderSystem.Api

### Configuration
```csharp
var orderApi = builder.AddProject<Projects.OrderSystem_Api>("order-api")
    .WithReference(redis)          // Auto-inject Redis connection
    .WithReference(nats)            // Auto-inject NATS connection
    .WithReplicas(3)                // 3 replicas for HA
    .WithHttpEndpoint(port: 5000)   // HTTP endpoint
    .WithHealthCheck();             // Auto health monitoring
```

### Replicas
- **Count**: 3
- **Load Balancing**: Automatic
- **Service Discovery**: Automatic
- **Health Monitoring**: Automatic

---

## 📊 Observability

### OpenTelemetry Integration

**Tracing**:
- ASP.NET Core requests
- HTTP client calls
- Catga commands & events

**Metrics**:
- Request duration
- HTTP client metrics
- .NET runtime metrics
- Catga operation metrics

**Logs**:
- Structured logging
- Correlation IDs
- Trace context propagation

### Aspire Dashboard

Access at http://localhost:15888

**Features**:
- 📈 Real-time metrics
- 🔍 Distributed tracing
- 📋 Structured logs
- 🏥 Health status
- 🔄 Resource monitoring

---

## 🛡️ Resilience

### Built-in Patterns

**Retry**:
- Exponential backoff
- Max 3 attempts
- Transient error handling

**Circuit Breaker**:
- Open after 5 failures
- Half-open retry after 30s
- Auto-recovery

**Timeout**:
- 30s per request
- Cancellation propagation

---

## 🏥 Health Checks

### Endpoints

| Endpoint | Purpose | K8s Probe |
|----------|---------|-----------|
| `/health` | Overall health | Combined |
| `/health/live` | Liveness | Liveness |
| `/health/ready` | Readiness | Readiness |

### Integration

```yaml
# Kubernetes deployment.yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 5000
  initialDelaySeconds: 10

readinessProbe:
  httpGet:
    path: /health/ready
    port: 5000
  initialDelaySeconds: 5
```

---

## 🎯 Service Discovery

### Auto-Configuration

Services automatically discover each other:

```csharp
// In OrderSystem.Api
var httpClient = httpClientFactory.CreateClient("inventory-service");
// Automatically resolves to: http://inventory-api:5001
```

### DNS Resolution

Format: `{service-name}:{port}`

Examples:
- `order-api:5000`
- `redis:6379`
- `nats:4222`

---

## 🚀 Deployment

### Local Development

```bash
dotnet run
```

### Docker Deployment

```bash
dotnet publish -c Release
docker build -t order-system-apphost .
docker run -p 15888:15888 order-system-apphost
```

### Production

For production, deploy services directly to Kubernetes:
- Use `deployment.yaml` from OrderSystem.Api
- Remove AppHost (dev-only orchestrator)
- Use K8s native service discovery

---

## 📈 Performance

### Resource Usage (3 replicas)

| Component | Memory | CPU | Disk |
|-----------|--------|-----|------|
| Redis | ~50 MB | ~1% | 100 MB |
| NATS | ~30 MB | ~1% | 50 MB |
| OrderApi x3 | ~150 MB | ~15% | - |
| **Total** | **~230 MB** | **~17%** | **150 MB** |

### Startup Time
- Infrastructure: ~3s (Redis + NATS)
- Services: ~2s (3 replicas)
- Total: **~5s**

---

## 🐛 Troubleshooting

### Port Conflicts

```bash
# Check ports
netstat -ano | findstr "5000 6379 4222 15888"

# Kill process
taskkill /PID <pid> /F
```

### Dashboard Not Loading

- Check OTEL_EXPORTER_OTLP_ENDPOINT environment variable
- Verify Aspire Dashboard is running
- Check logs in console output

### Service Not Starting

1. Check container logs in Aspire Dashboard
2. Verify port availability
3. Check health endpoints
4. Review application logs

---

## 📚 Related Documentation

- [OrderSystem.Api](../OrderSystem.Api/README.md) - Service implementation
- [Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)

---

<div align="center">

**🎉 One Command, Full Cluster!**

`dotnet run` → Redis + NATS + 3-replica API + Dashboard

[Main README](../../docs/README.md) · [OrderSystem.Api](../OrderSystem.Api/README.md)

</div>
