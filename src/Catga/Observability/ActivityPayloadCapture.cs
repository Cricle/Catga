using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Observability;

/// <summary>
/// Utility class for capturing message/event payloads in Activity tags for debugging.
/// Supports both reflection-based and AOT-compatible serialization.
/// </summary>
public static class ActivityPayloadCapture
{
    private const int MaxPayloadLength = 4096;

    /// <summary>
    /// Optional custom serializer for rich payload capture.
    /// If not set, falls back to ToString() (always AOT-safe).
    /// Set this to a custom serializer for better debugging experience.
    /// </summary>
    public static Func<object, string>? CustomSerializer { get; set; }

    /// <summary>
    /// Captures a payload in an Activity tag.
    /// Uses CustomSerializer if available, otherwise uses ToString() (always AOT-safe).
    /// </summary>
    /// <typeparam name="T">Type of payload to serialize</typeparam>
    /// <param name="activity">Activity to attach the tag to</param>
    /// <param name="tagName">Name of the tag</param>
    /// <param name="payload">Payload to serialize</param>
    public static void CapturePayload<T>(Activity? activity, string tagName, T payload)
    {
        if (activity == null || payload == null) return;

        string? json = null;

        // Try custom serializer first (AOT-safe, user-provided)
        if (CustomSerializer != null)
        {
            try
            {
                json = CustomSerializer(payload);
            }
            catch
            {
                // Custom serializer failed, set error indicator
                activity.SetTag(tagName, "<serialization error>");
                return;
            }
        }
        else
        {
            // Fallback to ToString() (always AOT-safe, no reflection needed)
            // Users should set CustomSerializer for better serialization
            json = payload.ToString();
        }

        if (json != null)
        {
            if (json.Length <= MaxPayloadLength)
            {
                activity.SetTag(tagName, json);
            }
            else
            {
                // Payload too large - just indicate the size
                activity.SetTag(tagName, $"<too large: {json.Length} bytes>");
            }
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

/// <summary>
/// Example: Set custom serializer for AOT compatibility
///
/// <code>
/// // In Program.cs (for MemoryPack users)
/// ActivityPayloadCapture.CustomSerializer = obj =>
/// {
///     if (obj is IMemoryPackable memoryPackable)
///         return Convert.ToBase64String(MemoryPackSerializer.Serialize(memoryPackable));
///     return obj.ToString() ?? string.Empty;
/// };
/// </code>
/// </summary>
internal static class ActivityPayloadCaptureExample
{
    // This class is just for documentation, not actual code
}

