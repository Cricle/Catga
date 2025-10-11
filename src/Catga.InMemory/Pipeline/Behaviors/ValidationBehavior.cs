using Catga.Exceptions;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Validation behavior for requests
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
        : base(logger)
    {
        _validators = validators;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Pipeline behaviors may require types that cannot be statically analyzed.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Pipeline behaviors use reflection for handler resolution.")]
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Simplified validation - avoid unnecessary collection operations
        if (!_validators.Any())
            return await next();

        var errors = new List<string>();
        foreach (var validator in _validators)
            errors.AddRange(await validator.ValidateAsync(request, cancellationToken));

        if (errors.Count > 0)
        {
            var messageId = TryGetMessageId(request) ?? "N/A";
            LogWarning("Validation failed for {RequestType}, MessageId: {MessageId}, Errors: {Errors}",
                GetRequestName(), messageId, string.Join("; ", errors));

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
    public Task<List<string>> ValidateAsync(T request, CancellationToken cancellationToken = default);
}

