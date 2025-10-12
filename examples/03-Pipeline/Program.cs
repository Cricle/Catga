using Catga;
using Catga.DependencyInjection;
using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.DependencyInjection;

// === 消息 ===
public record CalculateCommand(int A, int B) : IRequest<int>;

// === Handler ===
public class CalculateHandler : IRequestHandler<CalculateCommand, int>
{
    public Task<CatgaResult<int>> Handle(CalculateCommand request, CancellationToken ct)
    {
        var result = request.A + request.B;
        return Task.FromResult(CatgaResult<int>.Success(result));
    }
}

// === 自定义 Behavior ===
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<CatgaResult<TResponse>> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        Console.WriteLine($"→ Before: {typeof(TRequest).Name}");
        var result = await next();
        Console.WriteLine($"← After: Success={result.IsSuccess}");
        return result;
    }
}

public class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<CatgaResult<TResponse>> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await next();
        sw.Stop();
        Console.WriteLine($"⏱️  Elapsed: {sw.ElapsedMilliseconds}ms");
        return result;
    }
}

// === 配置 ===
var services = new ServiceCollection();
services.AddCatga();
services.AddHandler<CalculateCommand, int, CalculateHandler>();

// 注册 Behaviors（按注册顺序执行）
services.AddBehavior(typeof(LoggingBehavior<,>));
services.AddBehavior(typeof(TimingBehavior<,>));

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// === 运行 ===
Console.WriteLine("Executing command with pipeline:\n");
var result = await mediator.SendAsync(new CalculateCommand(10, 20));
Console.WriteLine($"\n✅ Result: {result.Data}");

