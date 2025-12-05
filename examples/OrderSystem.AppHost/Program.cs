var builder = DistributedApplication.CreateBuilder(args);

// ===== Infrastructure Configuration =====

// Jaeger - Distributed tracing
var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one", "latest")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "jaeger-ui")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otlp-http")
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
    .WithLifetime(ContainerLifetime.Persistent);

// Redis - Distributed cache, locks, idempotency, outbox
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisCommander()
    .WithLifetime(ContainerLifetime.Persistent);

// NATS - Distributed messaging with JetStream
var nats = builder.AddNats("nats")
    .WithDataVolume()
    .WithJetStream()
    .WithLifetime(ContainerLifetime.Persistent);

// ===== Cluster Configuration =====
// Enable replicas for distributed demo (set CLUSTER_MODE=true to enable)
var clusterMode = Environment.GetEnvironmentVariable("CLUSTER_MODE") == "true";
var replicaCount = clusterMode ? 3 : 1;

// OrderSystem API - Order service with optional clustering
var orderApi = builder.AddProject<Projects.OrderSystem_Api>("order-api")
    .WithReference(redis)
    .WithReference(nats)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WithEnvironment("OTEL_SERVICE_NAME", "order-api")
    .WithEnvironment("Catga__NodeId", "node-1")
    .WithEnvironment("Catga__ClusterEnabled", clusterMode.ToString().ToLower())
    .WithReplicas(replicaCount);

// ===== Aspire Features =====
//
// Auto-enabled features:
//   - OpenTelemetry: Tracing, metrics, logs -> Aspire Dashboard + Jaeger
//   - Service Discovery: Auto endpoint resolution
//   - Health Checks: /health, /health/live, /health/ready
//   - Resilience: Retry, circuit breaker, timeout
//   - Graceful Lifecycle: Catga shutdown & recovery
//
// Monitoring URLs:
//   - Aspire Dashboard: http://localhost:15888 (System monitoring)
//   - Jaeger UI:        http://localhost:16686 (Distributed tracing)
//   - Redis Commander:  http://localhost:8081  (Redis management)
//   - OrderSystem UI:   http://localhost:5275  (Business operations)
//
// Jaeger Search Tags:
//   - catga.type=command  (View all commands)
//   - catga.type=event    (View all events)
//   - catga.type=flow     (View all distributed flows)
//
// Cluster Mode:
//   Set CLUSTER_MODE=true to run 3 replicas for distributed demo
//   Example: $env:CLUSTER_MODE="true"; dotnet run

builder.Build().Run();
