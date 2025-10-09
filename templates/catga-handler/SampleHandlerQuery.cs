using Catga;

namespace YourNamespace.Queries;

/// <summary>
/// Query to get SampleHandler data
/// </summary>
public record SampleHandlerQuery(
    long Id
) : IRequest<SampleHandlerResult>;

public record SampleHandlerResult(
    long Id,
    string Name,
    int Value,
    DateTime CreatedAt
);

/// <summary>
/// Handler for SampleHandlerQuery
/// </summary>
public class SampleHandlerQueryHandler : IRequestHandler<SampleHandlerQuery, SampleHandlerResult>
{
    private readonly ILogger<SampleHandlerQueryHandler> _logger;

    public SampleHandlerQueryHandler(ILogger<SampleHandlerQueryHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<SampleHandlerResult> Handle(
        SampleHandlerQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling SampleHandlerQuery: Id={Id}",
            request.Id);

        // TODO: Query your data source here
        await Task.Delay(5, cancellationToken); // Simulate query

        var result = new SampleHandlerResult(
            request.Id,
            "Sample Name",
            42,
            DateTime.UtcNow);

        _logger.LogInformation(
            "SampleHandlerQuery completed successfully: Id={Id}",
            request.Id);

        return result;
    }
}

