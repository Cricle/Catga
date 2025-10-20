using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Validation behavior</summary>
public class ValidationBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators, ILogger<ValidationBehavior<TRequest, TResponse>> logger)
        : base(logger) => _validators = validators;

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (!_validators.Any()) return await next();

        var errors = new List<string>();
        foreach (var validator in _validators)
            errors.AddRange(await validator.ValidateAsync(request, cancellationToken));

        if (errors.Count > 0)
        {
            var messageId = TryGetMessageId(request)?.ToString() ?? "N/A";
            LogWarning("Validation failed for {RequestType}, MessageId: {MessageId}, Errors: {Errors}",
                GetRequestName(), messageId, string.Join("; ", errors));
            return CatgaResult<TResponse>.Failure("Validation failed", new CatgaValidationException("Validation failed", errors));
        }
        return await next();
    }
}

public interface IValidator<in T>
{
    Task<List<string>> ValidateAsync(T request, CancellationToken cancellationToken = default);
}

