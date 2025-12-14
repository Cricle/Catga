using System.Diagnostics;
using Catga.Observability;

namespace Catga.Transport;

/// <summary>
/// Helper for setting up transport-related Activity tags.
/// </summary>
public static class TransportActivityHelper
{
    /// <summary>
    /// Set common publish operation tags on an Activity.
    /// </summary>
    public static void SetPublishTags(
        Activity? activity,
        string system,
        string destination,
        string messageType,
        string? messageId = null,
        string? qos = null)
    {
        if (activity == null) return;

        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, system);
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, destination);
        activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "publish");
        activity.SetTag(CatgaActivitySource.Tags.MessageType, messageType);

        if (messageId != null)
            activity.SetTag(CatgaActivitySource.Tags.MessageId, messageId);

        if (qos != null)
            activity.SetTag(CatgaActivitySource.Tags.QoS, qos);
    }

    /// <summary>
    /// Set common subscribe/receive operation tags on an Activity.
    /// </summary>
    public static void SetSubscribeTags(
        Activity? activity,
        string system,
        string source,
        string messageType,
        string? messageId = null)
    {
        if (activity == null) return;

        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, system);
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, source); // Use destination for source
        activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "receive");
        activity.SetTag(CatgaActivitySource.Tags.MessageType, messageType);

        if (messageId != null)
            activity.SetTag(CatgaActivitySource.Tags.MessageId, messageId);
    }

    /// <summary>
    /// Start a publish activity with common tags.
    /// </summary>
    public static Activity? StartPublishActivity(
        string system,
        string destination,
        string messageType,
        string? messageId = null,
        string? qos = null)
    {
        var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        SetPublishTags(activity, system, destination, messageType, messageId, qos);
        return activity;
    }
}
