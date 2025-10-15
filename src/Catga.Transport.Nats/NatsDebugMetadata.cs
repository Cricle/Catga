using Catga.Debugging;

namespace Catga.Transport.Nats;

/// <summary>
/// NATS debug metadata extractor - zero-copy, minimal overhead
/// </summary>
public static class NatsDebugMetadata
{
    /// <summary>
    /// Extract metadata from NATS subject and headers - no allocations
    /// </summary>
    public static void ExtractMetadata(string subject, Dictionary<string, string>? headers, Dictionary<string, string> metadata)
    {
        // NATS subject (zero-copy reference)
        metadata["nats.subject"] = subject;

        // Extract subject components (e.g., "orders.created.v1" → type: "orders", action: "created")
        var parts = subject.Split('.');
        if (parts.Length > 0)
            metadata["nats.type"] = parts[0];
        if (parts.Length > 1)
            metadata["nats.action"] = parts[1];

        // Extract trace context from headers (if available)
        if (headers != null)
        {
            if (headers.TryGetValue("traceparent", out var traceParent))
                metadata["traceparent"] = traceParent;

            if (headers.TryGetValue("correlation-id", out var correlationId))
                metadata["correlation_id"] = correlationId;
        }
    }
}

