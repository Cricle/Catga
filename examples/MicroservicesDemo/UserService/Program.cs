using Catga.AspNetCore.Rpc;
using Catga.Rpc;
using Catga.Serialization.MemoryPack;
using Catga.Transport.Nats;
using MicroservicesDemo.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNatsTransport("nats://localhost:4222");
builder.Services.AddMemoryPackSerializer();

builder.Services.AddCatgaRpcServer(options => { options.ServiceName = "UserService"; });

var app = builder.Build();

var rpcServer = app.Services.GetRequiredService<IRpcServer>();
rpcServer.RegisterHandler<GetUserRequest, GetUserResponse>("GetUser", async (request, ct) =>
{
    await Task.Delay(10, ct);
    return new GetUserResponse
    {
        UserId = request.UserId,
        UserName = $"User_{request.UserId}",
        Email = $"user{request.UserId}@example.com",
        IsActive = true
    };
});

rpcServer.RegisterHandler<ValidateUserRequest, ValidateUserResponse>("ValidateUser", async (request, ct) =>
{
    await Task.Delay(5, ct);
    return new ValidateUserResponse
    {
        IsValid = request.UserId > 0,
        Message = request.UserId > 0 ? "User valid" : "Invalid user ID"
    };
});

app.MapGet("/health", () => Results.Ok(new { service = "UserService", status = "healthy" }));

await app.RunAsync();

