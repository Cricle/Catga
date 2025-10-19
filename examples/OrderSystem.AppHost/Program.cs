var builder = DistributedApplication.CreateBuilder(args);

// ===== Infrastructure Configuration =====

// Jaeger - Distributed tracing (替代 Catga.Debugger)
var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one", "latest")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "jaeger-ui")   // Jaeger UI
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")         // OTLP gRPC
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otlp-http")         // OTLP HTTP
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
    .WithLifetime(ContainerLifetime.Persistent);

// Redis - Distributed cache, locks, idempotency
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisCommander()
    .WithLifetime(ContainerLifetime.Persistent);

// NATS - Distributed messaging with JetStream
var nats = builder.AddNats("nats")
    .WithDataVolume()
    .WithJetStream()
    .WithLifetime(ContainerLifetime.Persistent);

// ===== Microservices Configuration =====

// OrderSystem API - Order service (single instance for demo)

var orderApi = builder.AddProject<Projects.OrderSystem_Api>("order-api")
    .WithReference(redis)
    .WithReference(nats)
    // Jaeger OTLP endpoint configuration
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WithEnvironment("OTEL_SERVICE_NAME", "order-api");
// Note: HTTP endpoint is auto-configured by Aspire, no need to add manually

// ===== Aspire Features =====
//
// 🎯 Auto-enabled features:
//   ✅ OpenTelemetry - Tracing, metrics, logs → Aspire Dashboard + Jaeger
//   ✅ Service Discovery - Auto endpoint resolution
//   ✅ Health Checks - /health, /health/live, /health/ready
//   ✅ Resilience - Retry, circuit breaker, timeout
//   ✅ Graceful Lifecycle - Catga shutdown & recovery
//
// 📊 Monitoring:
//   - Aspire Dashboard: http://localhost:15888 (系统级监控)
//   - Jaeger UI:        http://localhost:16686 (分布式追踪 - 完整的Catga事务流程)
//   - OrderSystem UI:   http://localhost:5000  (业务操作)
//
// 🔍 在 Jaeger 中搜索:
//   - catga.type=command  (查看所有命令)
//   - catga.type=event    (查看所有事件)
//   - catga.type=catga    (查看所有分布式事务)
//
// 📖 See: README.md for complete guide

builder.Build().Run();
