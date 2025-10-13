var builder = DistributedApplication.CreateBuilder(args);

// Add Redis for distributed cache, lock, and cluster
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisCommander();

// Add NATS for distributed messaging and cluster
var nats = builder.AddNats("nats")
    .WithDataVolume()
    .WithJetStream();

// TODO: Add your Catga-based microservices here
// Example:
// var orderSystem = builder.AddProject<Projects.YourService>("yourservice")
//     .WithReference(redis)
//     .WithReference(nats)
//     .WithReplicas(3);

builder.Build().Run();
