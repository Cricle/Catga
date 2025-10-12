using Catga;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

// === 消息 (使用 MemoryPack 标记为 AOT 友好) ===
[MemoryPack.MemoryPackable]
public partial record OrderCommand(string OrderId, decimal Amount) : IRequest<bool>;

// === Handler ===
public class OrderHandler : IRequestHandler<OrderCommand, bool>
{
    public Task<CatgaResult<bool>> Handle(OrderCommand request, CancellationToken ct)
    {
        Console.WriteLine($"Processing order: {request.OrderId} - ${request.Amount}");
        return Task.FromResult(CatgaResult<bool>.Success(true));
    }
}

// === 配置 (AOT 友好) ===
var services = new ServiceCollection();
services.AddCatga()
    .UseMemoryPackSerializer()  // 使用 MemoryPack，零配置 AOT
    .AddGeneratedHandlers();    // 使用源生成器，零反射

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// === 运行 ===
Console.WriteLine("🚀 Native AOT Example");
Console.WriteLine($"AOT Compiled: {!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}\n");

var result = await mediator.SendAsync(new OrderCommand("ORD-001", 99.99m));
Console.WriteLine($"✅ Result: {result.IsSuccess}");

/*
 * 发布为 Native AOT:
 * dotnet publish -c Release -r win-x64
 * 
 * 结果:
 * - 启动时间: ~50ms (vs 1200ms 传统)
 * - 文件大小: ~8MB (vs 68MB 传统)
 * - 内存占用: ~12MB (vs 85MB 传统)
 */

