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
        // Manual enumeration check instead of LINQ Any()
        using var enumerator = _validators.GetEnumerator();
        if (!enumerator.MoveNext())
            return await next();

        var errors = new List<string>();

        // First validator (already have from enumerator)
        errors.AddRange(await enumerator.Current.ValidateAsync(request, cancellationToken));

        // Remaining validators
        while (enumerator.MoveNext())
            errors.AddRange(await enumerator.Current.ValidateAsync(request, cancellationToken));

        if (errors.Count > 0)
        {
            var messageId = TryGetMessageId(request)?.ToString() ?? "N/A";

            // Manual string concatenation instead of string.Join
            var errorMessage = errors.Count == 1 ? errors[0] : BuildErrorMessage(errors);

            LogWarning("Validation failed for {RequestType}, MessageId: {MessageId}, Errors: {Errors}",
                GetRequestName(), messageId, errorMessage);
            
            return CatgaResult<TResponse>.Failure(ErrorInfo.Validation("Validation failed", errorMessage));
        }
        return await next();
    }

    private static string BuildErrorMessage(List<string> errors)
    {
        var totalLength = 0;
        for (int i = 0; i < errors.Count; i++)
            totalLength += errors[i].Length;
        totalLength += (errors.Count - 1) * 2; // "; " separators

        return string.Create(totalLength, errors, (span, errs) =>
        {
            var pos = 0;
            for (int i = 0; i < errs.Count; i++)
            {
                if (i > 0)
                {
                    span[pos++] = ';';
                    span[pos++] = ' ';
                }
                errs[i].AsSpan().CopyTo(span[pos..]);
                pos += errs[i].Length;
            }
        });
    }
}

public interface IValidator<in T>
{
    Task<List<string>> ValidateAsync(T request, CancellationToken cancellationToken = default);
}

