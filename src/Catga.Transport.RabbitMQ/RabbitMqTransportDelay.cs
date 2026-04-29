using Catga.Abstractions;

namespace Catga.Transport.RabbitMQ;

internal static class RabbitMqTransportDelay
{
    internal const string DelayHeaderKey = "x-delay";
    private const string DelayedExchangeType = "x-delayed-message";
    private const string DelayedExchangeTypeArgument = "x-delayed-type";

    public static string ResolveExchangeType(RabbitMqTransportOptions options)
        => options.UseDelayedExchange ? DelayedExchangeType : options.ExchangeType;

    public static Dictionary<string, object?>? BuildExchangeArguments(RabbitMqTransportOptions options)
    {
        if (!options.UseDelayedExchange)
            return null;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [DelayedExchangeTypeArgument] = options.ExchangeType
        };
    }

    public static int? ResolveDelayMilliseconds(object message, TransportContext context)
    {
        if (context.Metadata is { Count: > 0 } metadata &&
            metadata.TryGetValue(DelayHeaderKey, out var rawDelay) &&
            int.TryParse(rawDelay, out var explicitDelayMs) &&
            explicitDelayMs > 0)
        {
            return explicitDelayMs;
        }

        if (message is not IDelayedMessage delayedMessage)
            return null;

        var delay = delayedMessage.DeliverAt - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
            return null;

        var totalMilliseconds = delay.TotalMilliseconds;
        if (totalMilliseconds >= int.MaxValue)
            return int.MaxValue;

        return Math.Max(1, (int)Math.Ceiling(totalMilliseconds));
    }
}
