namespace Catga.Transport.RabbitMQ;

internal static class RabbitMqTransportPriority
{
    internal const string PriorityMetadataKey = "x-priority";
    private const string MaxPriorityQueueArgument = "x-max-priority";

    public static Dictionary<string, object?>? BuildQueueArguments(RabbitMqTransportOptions options)
    {
        if (options.MaxPriority is not > 0)
            return null;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [MaxPriorityQueueArgument] = (int)options.MaxPriority.Value
        };
    }

    public static byte? ResolvePriority(TransportContext context, RabbitMqTransportOptions options)
    {
        if (context.Metadata is not { Count: > 0 } metadata ||
            !metadata.TryGetValue(PriorityMetadataKey, out var rawValue) ||
            !byte.TryParse(rawValue, out var parsedPriority))
        {
            return null;
        }

        if (options.MaxPriority is byte maxPriority)
            return parsedPriority > maxPriority ? maxPriority : parsedPriority;

        return parsedPriority;
    }
}
