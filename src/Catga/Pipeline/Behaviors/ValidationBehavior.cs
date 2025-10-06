using Catga.Exceptions;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Validation behavior for requests
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // 简化验证 - 避免不必要的集合操作
        if (!_validators.Any())
            return await next();

        var errors = new List<string>();
        foreach (var validator in _validators)
            errors.AddRange(await validator.ValidateAsync(request, cancellationToken));

        if (errors.Count > 0)
        {
            _logger.LogWarning("Validation failed for {RequestType}, MessageId: {MessageId}, Errors: {Errors}",
                typeof(TRequest).Name, request.MessageId, string.Join("; ", errors));

            return CatgaResult<TResponse>.Failure("Validation failed",
                new CatgaValidationException("Validation failed", errors));
        }

        return await next();
    }
}

/// <summary>
/// Validator interface for requests
/// </summary>
public interface IValidator<in T>
{
    Task<List<string>> ValidateAsync(T request, CancellationToken cancellationToken = default);
}

