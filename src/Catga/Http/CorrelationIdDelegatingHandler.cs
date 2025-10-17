using System.Diagnostics;

namespace Catga.Http;

/// <summary>
/// HTTP DelegatingHandler that automatically propagates CorrelationId to downstream services
/// </summary>
/// <remarks>
/// This handler ensures that the CorrelationId from Activity.Current.Baggage is automatically
/// injected into outgoing HTTP requests as the X-Correlation-ID header.
///
/// This enables full distributed tracing across services:
/// Service A (HTTP Request) → Command → Event → Service B (HTTP Request) → Command → Event
///
/// The entire chain will share the same CorrelationId for end-to-end tracing in Jaeger.
/// </remarks>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private const string BaggageKey = "catga.correlation_id";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Propagate CorrelationId from Activity.Current.Baggage to HTTP header
        var activity = Activity.Current;
        if (activity != null)
        {
            var correlationId = activity.GetBaggageItem(BaggageKey);
            if (!string.IsNullOrEmpty(correlationId))
            {
                // Only add if not already present (allow explicit override)
                if (!request.Headers.Contains(CorrelationIdHeaderName))
                {
                    request.Headers.Add(CorrelationIdHeaderName, correlationId);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

