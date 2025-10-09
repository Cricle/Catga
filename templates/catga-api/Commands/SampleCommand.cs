using Catga.Messages;
using Catga.Handlers;
using Catga.Results;
using Catga.SourceGenerator;

namespace CatgaApi.Commands;

/// <summary>
/// Sample command demonstrating Catga usage
/// </summary>
[GenerateMessageContract]
public partial record SampleCommand(string Name, string Description) : IRequest<SampleResponse>;

/// <summary>
/// Response for SampleCommand
/// </summary>
public record SampleResponse(Guid Id, string Message, DateTime CreatedAt);

/// <summary>
/// Handler for SampleCommand
/// </summary>
public class SampleCommandHandler : IRequestHandler<SampleCommand, SampleResponse>
{
    private readonly ILogger<SampleCommandHandler> _logger;

    public SampleCommandHandler(ILogger<SampleCommandHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<CatgaResult<SampleResponse>> Handle(
        SampleCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing SampleCommand: {Name}", request.Name);

        // Simulate some async work
        await Task.Delay(10, cancellationToken);

        var response = new SampleResponse(
            Id: Guid.NewGuid(),
            Message: $"Processed: {request.Name} - {request.Description}",
            CreatedAt: DateTime.UtcNow);

        return CatgaResult<SampleResponse>.Success(response);
    }
}

