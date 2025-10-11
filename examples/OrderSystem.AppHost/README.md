# OrderSystem.AppHost - .NET Aspire Orchestration

**.NET Aspire** orchestration host for OrderSystem, providing automatic service discovery, container orchestration, and cloud-ready deployment.

## 🎯 Features

- ✅ **Automatic Container Orchestration** - Redis & NATS automatically started
- ✅ **Service Discovery** - Automatic endpoint resolution
- ✅ **Health Checks** - Built-in health monitoring
- ✅ **Observability** - Integrated telemetry, metrics, and logging
- ✅ **Resilience** - Built-in retry, circuit breaker, and timeout policies
- ✅ **Multi-Instance** - Run 3 OrderSystem replicas for high availability

## 🚀 Quick Start

```bash
cd examples/OrderSystem.AppHost
dotnet run
```

This will:
1. Start Redis container with persistent volume
2. Start NATS container with JetStream enabled
3. Launch 3 OrderSystem instances (ports 5001, 5002, 5003)
4. Open Aspire Dashboard at http://localhost:15000

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────┐
│          .NET Aspire Dashboard                  │
│         http://localhost:15000                  │
└─────────────────────────────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        ↓               ↓               ↓
  ┌──────────┐    ┌──────────┐    ┌──────────┐
  │OrderSys-1│    │OrderSys-2│    │OrderSys-3│
  │  :5001   │    │  :5002   │    │  :5003   │
  └──────────┘    └──────────┘    └──────────┘
        │               │               │
        └───────────────┼───────────────┘
                        │
        ┌───────────────┼───────────────┐
        ↓               ↓               ↓
  ┌──────────┐    ┌──────────┐    ┌──────────┐
  │  Redis   │    │   NATS   │    │ SQLite   │
  │  :6379   │    │  :4222   │    │ (local)  │
  └──────────┘    └──────────┘    └──────────┘
```

## 📊 Aspire Dashboard

Access the dashboard at **http://localhost:15000** to view:

- **Services**: All running services and their health
- **Logs**: Centralized logging from all services
- **Traces**: Distributed tracing across services
- **Metrics**: Performance metrics and resource usage
- **Containers**: Container status and resource consumption

## 🔧 Configuration

### Ports

- **15000** - Aspire Dashboard
- **16686** - OpenTelemetry endpoint
- **17000** - Resource service endpoint
- **5001-5003** - OrderSystem instances
- **6379** - Redis
- **4222** - NATS
- **8222** - NATS monitoring

### Environment Variables

Configure in `appsettings.json` or override via environment:

```bash
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:DOTNET_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:16686"
dotnet run
```

## 🐳 Containers

### Redis
- **Image**: `redis:latest`
- **Features**: Persistent volume, Redis Commander UI
- **Connection**: Service discovery resolves to `redis`

### NATS
- **Image**: `nats:latest`
- **Features**: JetStream enabled, persistent volume
- **Connection**: Service discovery resolves to `nats`

## 🎮 Usage

### Start the Application

```bash
dotnet run
```

### Test Load Balancing

```powershell
# Test requests are distributed across 3 instances
1..10 | ForEach-Object {
    Invoke-RestMethod -Uri "http://localhost:5001/health"
}
```

### View Logs

```bash
# All logs centralized in Aspire Dashboard
# Open http://localhost:15000 → Logs tab
```

### Monitor Performance

```bash
# Metrics available in Aspire Dashboard
# Open http://localhost:15000 → Metrics tab
```

## 🔄 Comparison with Other Modes

| Feature | Standalone | Redis | NATS | **Aspire** |
|---------|------------|-------|------|----------|
| **Setup** | One command | Manual containers | Manual containers | **One command** |
| **Service Discovery** | ❌ | Manual | Manual | **✅ Automatic** |
| **Observability** | Basic logs | Basic logs | Basic logs | **✅ Full telemetry** |
| **Health Checks** | Basic | Basic | Basic | **✅ Integrated** |
| **Resilience** | ❌ | Custom | Custom | **✅ Built-in** |
| **Scaling** | Manual | Manual | Manual | **✅ Declarative** |
| **Production Ready** | ❌ | ✅ | ✅ | **✅ Cloud-ready** |

## 📝 Notes

- Aspire is ideal for **development** and **cloud deployment**
- For local development without containers, use **Standalone** mode
- For production without Aspire, use **NATS** or **Redis** modes
- Aspire provides the best developer experience with minimal configuration

## 📚 Learn More

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Aspire GitHub](https://github.com/dotnet/aspire)
- [OrderSystem Main README](../OrderSystem/README.md)

