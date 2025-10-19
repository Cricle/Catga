using System.Diagnostics.CodeAnalysis;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Safe request handler base - users only write business logic, no try-catch needed
/// </summary>
public abstract class SafeRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    protected ILogger Logger { get; }

    protected SafeRequestHandler(ILogger logger) => Logger = logger;

    /// <summary>
    /// Framework handles exceptions automatically - users only implement business logic
    /// </summary>
    public async Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await HandleCoreAsync(request, cancellationToken);
            return CatgaResult<TResponse>.Success(result);
        }
        catch (CatgaException ex)
        {
            return await OnBusinessErrorAsync(request, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            return await OnUnexpectedErrorAsync(request, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Handle business logic errors (CatgaException). Override to customize error handling.
    /// </summary>
    /// <param name="request">The original request</param>
    /// <param name="exception">The business exception</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failed result with error information</returns>
    protected virtual Task<CatgaResult<TResponse>> OnBusinessErrorAsync(TRequest request, CatgaException exception, CancellationToken cancellationToken)
    {
        Logger.LogWarning(exception, "Business logic failed: {Message}", exception.Message);
        return Task.FromResult(CatgaResult<TResponse>.Failure(exception.Message, exception));
    }

    /// <summary>
    /// Handle unexpected errors (non-CatgaException). Override to customize error handling.
    /// </summary>
    /// <param name="request">The original request</param>
    /// <param name="exception">The unexpected exception</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failed result with error information</returns>
    protected virtual Task<CatgaResult<TResponse>> OnUnexpectedErrorAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        Logger.LogError(exception, "Unexpected error in handler");
        return Task.FromResult(CatgaResult<TResponse>.Failure("Internal error", new CatgaException("Internal error", exception)));
    }

    /// <summary>
    /// Implement business logic here - throw CatgaException for business errors
    /// </summary>
    protected abstract Task<TResponse> HandleCoreAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Safe request handler base for commands without response
/// </summary>
public abstract class SafeRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
    protected ILogger Logger { get; }

    protected SafeRequestHandler(ILogger logger) => Logger = logger;

    public async Task<CatgaResult> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await HandleCoreAsync(request, cancellationToken);
            return CatgaResult.Success();
        }
        catch (CatgaException ex)
        {
            return await OnBusinessErrorAsync(request, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            return await OnUnexpectedErrorAsync(request, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Handle business logic errors (CatgaException). Override to customize error handling.
    /// </summary>
    /// <param name="request">The original request</param>
    /// <param name="exception">The business exception</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failed result with error information</returns>
    protected virtual Task<CatgaResult> OnBusinessErrorAsync(TRequest request, CatgaException exception, CancellationToken cancellationToken)
    {
        Logger.LogWarning(exception, "Business logic failed: {Message}", exception.Message);
        return Task.FromResult(CatgaResult.Failure(exception.Message, exception));
    }

    /// <summary>
    /// Handle unexpected errors (non-CatgaException). Override to customize error handling.
    /// </summary>
    /// <param name="request">The original request</param>
    /// <param name="exception">The unexpected exception</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failed result with error information</returns>
    protected virtual Task<CatgaResult> OnUnexpectedErrorAsync(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        Logger.LogError(exception, "Unexpected error in handler");
        return Task.FromResult(CatgaResult.Failure("Internal error", new CatgaException("Internal error", exception)));
    }

    /// <summary>
    /// Implement business logic here - throw CatgaException for business errors
    /// </summary>
    protected abstract Task HandleCoreAsync(TRequest request, CancellationToken cancellationToken);
}
