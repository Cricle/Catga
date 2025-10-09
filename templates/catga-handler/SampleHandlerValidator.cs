using Catga;
using System.ComponentModel.DataAnnotations;

namespace YourNamespace.Validators;

/// <summary>
/// Validator for SampleHandlerCommand
/// </summary>
public class SampleHandlerValidator : IPipelineBehavior<SampleHandlerCommand, SampleHandlerResponse>
{
    public async ValueTask<SampleHandlerResponse> Handle(
        SampleHandlerCommand request,
        RequestHandlerDelegate<SampleHandlerResponse> next,
        CancellationToken cancellationToken)
    {
        // Validate Name
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Name is required");
        }

        if (request.Name.Length > 100)
        {
            throw new ValidationException("Name must be less than 100 characters");
        }

        // Validate Value
        if (request.Value < 0)
        {
            throw new ValidationException("Value must be non-negative");
        }

        if (request.Value > 1000)
        {
            throw new ValidationException("Value must be less than or equal to 1000");
        }

        // Validation passed, continue to handler
        return await next();
    }
}

