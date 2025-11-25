using System;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.DistributedId;
using Catga.Outbox;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

public class ConditionalOutboxBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ConditionalOutboxBehavior<TRequest, TResponse>> _logger;
    private OutboxBehavior<TRequest, TResponse>? _inner;

    public ConditionalOutboxBehavior(IServiceProvider sp, ILogger<ConditionalOutboxBehavior<TRequest, TResponse>> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (_inner == null)
        {
            var idGen = _sp.GetService<IDistributedIdGenerator>();
            var store = _sp.GetService<IOutboxStore>();
            var transport = _sp.GetService<IMessageTransport>();
            var serializer = _sp.GetService<IMessageSerializer>();
            if (idGen == null || store == null || transport == null || serializer == null)
                return await next();

            var innerLogger = _sp.GetRequiredService<ILogger<OutboxBehavior<TRequest, TResponse>>>();
            _inner = new OutboxBehavior<TRequest, TResponse>(innerLogger, idGen, store, transport, serializer);
        }

        return await _inner.HandleAsync(request, next, cancellationToken);
    }
}
