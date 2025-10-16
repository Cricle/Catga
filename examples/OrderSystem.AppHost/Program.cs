var builder = DistributedApplication.CreateBuilder(args);

// ===== Infrastructure Configuration =====

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
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(port: 5000, name: "http");        // HTTP endpoint on port 5000

// ===== Aspire Features =====
//
// 🎯 Auto-enabled features:
//   ✅ OpenTelemetry - Tracing, metrics, logs → Aspire Dashboard
//   ✅ Service Discovery - Auto endpoint resolution
//   ✅ Health Checks - /health, /health/live, /health/ready
//   ✅ Resilience - Retry, circuit breaker, timeout
//   ✅ Graceful Lifecycle - Catga shutdown & recovery
//
// 📊 Access Aspire Dashboard: http://localhost:15888
// 📊 Access OrderSystem UI: http://localhost:5000
// 📖 See: README.md for complete guide

builder.Build().Run();
