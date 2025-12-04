using Catga;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Outbox processor - only runs on cluster leader.
///
/// Framework auto-generates:
/// - Leader check before execution
/// - Forward to leader if not leader
/// - Telemetry and metrics
/// </summary>
[CatgaHandler]
[Route("/outbox/process")]
[LeaderOnly]
[Retry(MaxAttempts = 5)]
public sealed partial class ProcessOutboxHandler(
    ILogger<ProcessOutboxHandler> logger) : IRequestHandler<ProcessOutboxCommand>
{
    private async Task<CatgaResult> HandleAsyncCore(
        ProcessOutboxCommand request, CancellationToken ct)
    {
        logger.LogInformation("Processing outbox batch: {BatchSize} messages", request.BatchSize);

        // Simulate outbox processing
        await Task.Delay(100, ct);

        logger.LogInformation("Outbox batch processed successfully");
        return CatgaResult.Success();
    }
}

/// <summary>
/// Command to process outbox messages.
/// </summary>
public record ProcessOutboxCommand(int BatchSize = 100) : IRequest
{
    public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
}
