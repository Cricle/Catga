var builder = DistributedApplication.CreateBuilder(args);

// Add Redis for distributed cache, lock, and cluster
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisCommander();

// Add NATS for distributed messaging and cluster
var nats = builder.AddNats("nats")
    .WithDataVolume()
    .WithJetStream();

// Add OrderSystem API with different deployment profiles
var orderSystem = builder.AddProject<Projects.OrderSystem>("ordersystem")
    .WithReference(redis)
    .WithReference(nats)
    .WithEnvironment("DeploymentMode", "Aspire")
    .WithReplicas(3);  // Run 3 instances for cluster demo

builder.Build().Run();

