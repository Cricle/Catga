using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that writes failed requests to the configured dead letter queue.
/// Registration alone does not require a DLQ implementation; if none is registered, the behavior is skipped.
/// </summary>
public sealed class DeadLetterBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private const int UnknownRetryCount = 0;
    private readonly IServiceProvider _serviceProvider;

    public DeadLetterBehavior(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await next();

            if (!result.IsSuccess)
                await TryWriteDeadLetterAsync(request, ToException(result), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await TryWriteDeadLetterAsync(request, ex, cancellationToken);
            throw;
        }
    }

    private async Task TryWriteDeadLetterAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var deadLetterQueue = _serviceProvider.GetService<IDeadLetterQueue>();
            if (deadLetterQueue != null)
                await deadLetterQueue.SendAsync(request, exception, UnknownRetryCount, cancellationToken);
        }
        catch
        {
            // DLQ persistence must not mask the original pipeline result/exception.
        }
    }

    private static Exception ToException(CatgaResult<TResponse> result)
    {
        if (result.Exception != null)
            return result.Exception;

        return new CatgaException(
            string.IsNullOrWhiteSpace(result.Error) ? "Request processing failed." : result.Error,
            result.ErrorCode);
    }
}

/// <summary>
/// Non-response variant for IRequest commands so DLQ behavior also applies to fire-and-forget commands.
/// </summary>
public sealed class DeadLetterBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>
    : IPipelineBehavior<TRequest>
    where TRequest : class, IRequest
{
    private const int UnknownRetryCount = 0;
    private readonly IServiceProvider _serviceProvider;

    public DeadLetterBehavior(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async ValueTask<CatgaResult> HandleAsync(
        TRequest request,
        PipelineDelegate next,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await next();

            if (!result.IsSuccess)
                await TryWriteDeadLetterAsync(request, ToException(result), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await TryWriteDeadLetterAsync(request, ex, cancellationToken);
            throw;
        }
    }

    private async Task TryWriteDeadLetterAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var deadLetterQueue = _serviceProvider.GetService<IDeadLetterQueue>();
            if (deadLetterQueue != null)
                await deadLetterQueue.SendAsync(request, exception, UnknownRetryCount, cancellationToken);
        }
        catch
        {
            // DLQ persistence must not mask the original pipeline result/exception.
        }
    }

    private static Exception ToException(CatgaResult result)
    {
        if (result.Exception != null)
            return result.Exception;

        return new CatgaException(
            string.IsNullOrWhiteSpace(result.Error) ? "Request processing failed." : result.Error,
            result.ErrorCode);
    }
}
