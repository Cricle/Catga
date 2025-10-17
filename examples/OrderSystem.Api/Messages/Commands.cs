using Catga.Messages;
using Catga.Results;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

/// <summary>
/// Create order command (with auto-generated debug capture via Source Generator)
/// Demonstrates: CQRS pattern, automatic error handling, rollback mechanism
/// </summary>
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId,
    List<OrderItem> Items,
    string ShippingAddress,
    string PaymentMethod
) : IRequest<OrderCreatedResult>;

[MemoryPackable]
public partial record OrderCreatedResult(
    string OrderId,
    decimal TotalAmount,
    DateTime CreatedAt
);

/// <summary>
/// Cancel order command
/// Demonstrates: Simple command handling, state transitions
/// </summary>
[MemoryPackable]
public partial record CancelOrderCommand(
    string OrderId,
    string Reason
) : IRequest;

/// <summary>
/// Get order query
/// Demonstrates: Query pattern, read model
/// </summary>
[MemoryPackable]
public partial record GetOrderQuery(
    string OrderId
) : IRequest<Order?>;

// ===== 扩展指南 =====
// 💡 如何添加新命令？
//
// 1. 定义命令 Record：
//    [MemoryPackable]
//    public partial record MyCommand(string Param) : IRequest<MyResult>;
//
// 2. 创建 Handler（自动注册）：
//    public class MyCommandHandler : IRequestHandler<MyCommand, MyResult>
//    {
//        public async Task<CatgaResult<MyResult>> HandleAsync(MyCommand request, CancellationToken ct)
//        {
//            // 业务逻辑
//            return CatgaResult<MyResult>.Success(new MyResult());
//        }
//    }
//
// 3. 添加 API 端点（Program.cs）：
//    app.MapCatgaRequest<MyCommand, MyResult>("/api/my-endpoint");
//
// 就这么简单！Source Generator 会自动发现并注册。
//
// 示例：添加 ConfirmOrder 命令
// [MemoryPackable]
// public partial record ConfirmOrderCommand(string OrderId) : IRequest;
//
// public class ConfirmOrderHandler : IRequestHandler<ConfirmOrderCommand>
// {
//     private readonly IOrderRepository _repo;
//     public ConfirmOrderHandler(IOrderRepository repo) => _repo = repo;
//
//     public async Task<CatgaResult> HandleAsync(ConfirmOrderCommand request, CancellationToken ct)
//     {
//         var order = await _repo.GetByIdAsync(request.OrderId, ct);
//         if (order == null) return CatgaResult.Failure("Order not found");
//
//         order.Confirm();
//         await _repo.SaveAsync(order, ct);
//         return CatgaResult.Success();
//     }
// }
