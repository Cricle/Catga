using Catga.AspNetCore.Rpc;
using Catga.Rpc;
using Catga.Serialization.MemoryPack;
using Catga.Transport.Nats;
using MicroservicesDemo.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNatsTransport("nats://localhost:4222");
builder.Services.AddMemoryPackSerializer();

builder.Services.AddCatgaRpcClient(options => { options.ServiceName = "OrderService"; });

var app = builder.Build();

app.MapPost("/orders", async (CreateOrderRequest request, IRpcClient rpcClient) =>
{
    var validateResult = await rpcClient.CallAsync<ValidateUserRequest, ValidateUserResponse>(
        "UserService",
        "ValidateUser",
        new ValidateUserRequest { UserId = request.UserId },
        TimeSpan.FromSeconds(5));

    if (!validateResult.IsSuccess || !validateResult.Value!.IsValid) return Results.BadRequest(new { error = "User validation failed", message = validateResult.Value?.Message });
    var getUserResult = await rpcClient.CallAsync<GetUserRequest, GetUserResponse>("UserService", "GetUser", new GetUserRequest { UserId = request.UserId });
    if (!getUserResult.IsSuccess) return Results.Problem("Failed to get user info");

    var order = new
    {
        orderId = Guid.NewGuid(),
        userId = request.UserId,
        userName = getUserResult.Value!.UserName,
        userEmail = getUserResult.Value.Email,
        items = request.Items,
        totalAmount = request.TotalAmount,
        createdAt = DateTime.UtcNow
    };

    return Results.Ok(order);
});

app.MapGet("/health", () => Results.Ok(new { service = "OrderService", status = "healthy" }));

await app.RunAsync();

public record CreateOrderRequest(int UserId, string[] Items, decimal TotalAmount);

