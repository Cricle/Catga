using Catga;

namespace YourNamespace.Commands;

/// <summary>
/// Command to perform SampleHandler operation
/// </summary>
public record SampleHandlerCommand(
    string Name,
    int Value
) : IRequest<SampleHandlerResponse>;

public record SampleHandlerResponse(
    long Id,
    string Message,
    DateTime Timestamp
);

/// <summary>
/// Handler for SampleHandlerCommand
/// </summary>
public class SampleHandlerCommandHandler : IRequestHandler<SampleHandlerCommand, SampleHandlerResponse>
{
    private readonly ILogger<SampleHandlerCommandHandler> _logger;

    public SampleHandlerCommandHandler(ILogger<SampleHandlerCommandHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<SampleHandlerResponse> Handle(
        SampleHandlerCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling SampleHandlerCommand: Name={Name}, Value={Value}",
            request.Name,
            request.Value);

        // TODO: Add your business logic here
        await Task.Delay(10, cancellationToken); // Simulate work

        var id = Random.Shared.NextInt64(1, long.MaxValue);
        var message = $"Processed {request.Name} with value {request.Value}";

        _logger.LogInformation(
            "SampleHandlerCommand completed successfully: Id={Id}",
            id);

        return new SampleHandlerResponse(
            id,
            message,
            DateTime.UtcNow);
    }
}

