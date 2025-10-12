using Catga;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

// 1. 定义消息
public record SayHelloCommand(string Name) : IRequest<string>;

// 2. 实现 Handler
public class SayHelloHandler : IRequestHandler<SayHelloCommand, string>
{
    public Task<CatgaResult<string>> Handle(SayHelloCommand request, CancellationToken cancellationToken)
        => Task.FromResult(CatgaResult<string>.Success($"Hello, {request.Name}!"));
}

// 3. 配置和使用
var services = new ServiceCollection();
services.AddCatga();
services.AddHandler<SayHelloCommand, string, SayHelloHandler>();

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// 发送命令
var result = await mediator.SendAsync(new SayHelloCommand("World"));
Console.WriteLine(result.Data); // Output: Hello, World!

