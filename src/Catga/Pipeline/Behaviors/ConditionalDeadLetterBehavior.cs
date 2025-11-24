using System;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

public class ConditionalDeadLetterBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ConditionalDeadLetterBehavior<TRequest, TResponse>> _logger;

    public ConditionalDeadLetterBehavior(IServiceProvider sp, ILogger<ConditionalDeadLetterBehavior<TRequest, TResponse>> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var result = await next();
        if (result.IsSuccess)
            return result;

        var dlq = _sp.GetService<IDeadLetterQueue>();
        if (dlq == null)
            return result;

        try
        {
            if (request is IMessage msg)
            {
                var ex = result.Exception ?? new Exception(result.Error ?? "Unknown error");
                await dlq.SendAsync(request, ex, retryCount: 0, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to DeadLetterQueue");
        }

        return result;
    }
}
