using System;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

public class ConditionalInboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ConditionalInboxBehavior<TRequest, TResponse>> _logger;
    private InboxBehavior<TRequest, TResponse>? _inner;

    public ConditionalInboxBehavior(IServiceProvider sp, ILogger<ConditionalInboxBehavior<TRequest, TResponse>> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (_inner == null)
        {
            var store = _sp.GetService<IInboxStore>();
            var serializer = _sp.GetService<IMessageSerializer>();
            if (store == null || serializer == null)
                return await next();

            var innerLogger = _sp.GetRequiredService<ILogger<InboxBehavior<TRequest, TResponse>>>();
            _inner = new InboxBehavior<TRequest, TResponse>(innerLogger, store, serializer);
        }

        return await _inner.HandleAsync(request, next, cancellationToken);
    }
}
