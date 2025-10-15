using System.Diagnostics.CodeAnalysis;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Safe request handler base - users only write business logic, no try-catch needed
/// </summary>
public abstract class SafeRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    protected ILogger Logger { get; }

    protected SafeRequestHandler(ILogger logger)
    {
        Logger = logger;
    }

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
            Logger.LogWarning(ex, "Business logic failed: {Message}", ex.Message);
            return CatgaResult<TResponse>.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error in handler");
            return CatgaResult<TResponse>.Failure("Internal error", new CatgaException("Internal error", ex));
        }
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

    protected SafeRequestHandler(ILogger logger)
    {
        Logger = logger;
    }

    public async Task<CatgaResult> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await HandleCoreAsync(request, cancellationToken);
            return CatgaResult.Success();
        }
        catch (CatgaException ex)
        {
            Logger.LogWarning(ex, "Business logic failed: {Message}", ex.Message);
            return CatgaResult.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error in handler");
            return CatgaResult.Failure("Internal error", new CatgaException("Internal error", ex));
        }
    }

    /// <summary>
    /// Implement business logic here - throw CatgaException for business errors
    /// </summary>
    protected abstract Task HandleCoreAsync(TRequest request, CancellationToken cancellationToken);
}
