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

    public async Task<TransitResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<TransitResult<TResponse>>> next,
        CancellationToken cancellationToken = default)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var errors = new List<string>();

        foreach (var validator in _validators)
        {
            var validationErrors = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(validationErrors);
        }

        if (errors.Any())
        {
            _logger.LogWarning(
                "Validation failed for {RequestType}, MessageId: {MessageId}, Errors: {Errors}",
                typeof(TRequest).Name, request.MessageId, string.Join("; ", errors));

            return TransitResult<TResponse>.Failure(
                "Validation failed",
                new TransitValidationException("Validation failed", errors));
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

