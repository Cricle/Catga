using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Utility class for capturing message/event payloads in Activity tags for debugging.
/// Supports both reflection-based and AOT-compatible serialization.
/// </summary>
public static class ActivityPayloadCapture
{
    private const int MaxPayloadLength = 4096;

    /// <summary>
    /// Custom serializer for payload capture (REQUIRED for payload capture to work).
    /// Must be set before using CaptureRequest/CaptureResponse/CaptureEvent.
    /// If not set, methods will throw InvalidOperationException.
    /// </summary>
    /// <example>
    /// // For MemoryPack users:
    /// ActivityPayloadCapture.CustomSerializer = obj => obj is IMemoryPackFormatterRegister mp
    ///     ? Convert.ToBase64String(MemoryPackSerializer.Serialize(mp.GetType(), mp))
    ///     : obj.ToString() ?? string.Empty;
    /// </example>
    public static Func<object, string>? CustomSerializer { get; set; }

    /// <summary>
    /// Captures a payload in an Activity tag using CustomSerializer.
    /// </summary>
    /// <typeparam name="T">Type of payload to serialize</typeparam>
    /// <param name="activity">Activity to attach the tag to</param>
    /// <param name="tagName">Name of the tag</param>
    /// <param name="payload">Payload to serialize</param>
    /// <exception cref="InvalidOperationException">Thrown when CustomSerializer is not set</exception>
    public static void CapturePayload<T>(Activity? activity, string tagName, T payload)
    {
        if (activity == null || payload == null) return;

        // Require CustomSerializer to be set - no fallback
        if (CustomSerializer == null)
        {
            throw new InvalidOperationException(
                $"ActivityPayloadCapture.CustomSerializer must be set before capturing payloads. " +
                $"This is required for AOT compatibility. " +
                $"Set it in your application startup (e.g., Program.cs).");
        }

        // Use custom serializer (user is responsible for AOT compatibility)
        var json = CustomSerializer(payload);

        if (json.Length <= MaxPayloadLength)
        {
            activity.SetTag(tagName, json);
        }
        else
        {
            // Payload too large - indicate the size
            activity.SetTag(tagName, $"<too large: {json.Length} bytes>");
        }
    }

    /// <summary>
    /// Captures request payload with standard tag name.
    /// Use CustomSerializer for AOT-compatible serialization.
    /// </summary>
    public static void CaptureRequest<TRequest>(Activity? activity, TRequest request)
    {
        CapturePayload(activity, "catga.request.payload", request);
    }

    /// <summary>
    /// Captures response payload with standard tag name.
    /// Use CustomSerializer for AOT-compatible serialization.
    /// </summary>
    public static void CaptureResponse<TResponse>(Activity? activity, TResponse response)
    {
        CapturePayload(activity, "catga.response.payload", response);
    }

    /// <summary>
    /// Captures event payload with standard tag name.
    /// Use CustomSerializer for AOT-compatible serialization.
    /// </summary>
    public static void CaptureEvent<TEvent>(Activity? activity, TEvent @event)
    {
        CapturePayload(activity, "catga.event.payload", @event);
    }
}