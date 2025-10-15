var builder = DistributedApplication.CreateBuilder(args);

// ===== 基础设施配置 =====

// Redis - 分布式缓存、锁、幂等性
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisCommander();

// NATS - 分布式消息传输
var nats = builder.AddNats("nats")
    .WithDataVolume()
    .WithJetStream();

// ===== 微服务配置 =====

// OrderSystem API - 订单服务（3 副本集群）
var orderApi = builder.AddProject<Projects.OrderSystem_Api>("order-api")
    .WithReference(redis)
    .WithReference(nats)
    .WithReplicas(3)                     // 3 个副本，自动负载均衡
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(port: 5000, name: "http");

// ===== 配置说明 =====
//
// 🎯 Catga 优雅停机和恢复已自动启用（在 OrderSystem.Api/Program.cs）
//
// 自动获得的能力：
//   ✅ 优雅停机 - 等待进行中的请求完成
//   ✅ 自动恢复 - 连接断开时自动重连
//   ✅ 健康检查 - 自动监控组件状态
//   ✅ 负载均衡 - 3 副本自动分发请求
//   ✅ 服务发现 - 自动注册和解析端点
//   ✅ 零配置 - 无需理解复杂的分布式概念
//
// 📖 详见: README-GRACEFUL.md

builder.Build().Run();
