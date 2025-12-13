using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Validation behavior</summary>
public partial class ValidationBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    // ========== Fields ==========

    private readonly IValidator<TRequest>[] _validators;

    // ========== Constructor ==========

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators, ILogger<ValidationBehavior<TRequest, TResponse>> logger)
        : base(logger) => _validators = validators.ToArray();

    // ========== Public API ==========

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (_validators.Length == 0)
            return await next();

        var errors = new List<string>();

        foreach (var item in _validators)
            errors.AddRange(await item.ValidateAsync(request, cancellationToken));

        if (errors.Count > 0)
        {
            var errorMessage = errors.Count == 1 ? errors[0] : string.Join(";", errors);
            var messageId = TryGetMessageId(request)?.ToString() ?? "N/A";
            LogValidationFailed(Logger, GetRequestName(), messageId, errorMessage);

            return CatgaResult<TResponse>.Failure(ErrorInfo.Validation("Validation failed", errorMessage));
        }
        return await next();
    }

    [LoggerMessage(Message = "Validation failed for {requestType}, MessageId: {messageId}, Errors: {errors}", Level = LogLevel.Warning)]
    static partial void LogValidationFailed(ILogger logger, string requestType, string messageId, string errors);
}

public interface IValidator<in T>
{
    ValueTask<List<string>> ValidateAsync(T request, CancellationToken cancellationToken = default);
}

