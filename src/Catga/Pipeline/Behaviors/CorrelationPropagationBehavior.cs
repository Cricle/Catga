using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that sets the ambient CorrelationId from the incoming message.
/// All child messages (commands/events) published during handler execution
/// will inherit this CorrelationId automatically.
/// Register via services.AddCatga().WithCorrelationPropagation().
/// </summary>
public sealed class CorrelationPropagationBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICorrelationContext _context;

    public CorrelationPropagationBehavior(ICorrelationContext context) => _context = context;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Set correlation from message: use existing CorrelationId or fall back to MessageId
        var correlationId = request.CorrelationId ?? request.MessageId;
        _context.Set(correlationId);

        try
        {
            return await next();
        }
        finally
        {
            _context.Clear();
        }
    }
}
