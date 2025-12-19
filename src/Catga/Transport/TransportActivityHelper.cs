using System.Diagnostics;
using Catga.Observability;

namespace Catga.Transport;

/// <summary>Helper for setting up transport-related Activity tags.</summary>
public static class TransportActivityHelper
{
    public static void SetPublishTags(Activity? activity, string system, string destination, string messageType, string? messageId = null)
    {
        if (activity == null) return;
        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, system);
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, destination);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, messageType);
        if (messageId != null)
            activity.SetTag(CatgaActivitySource.Tags.MessageId, messageId);
    }

    public static void SetSubscribeTags(Activity? activity, string system, string source, string messageType, string? messageId = null)
    {
        if (activity == null) return;
        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, system);
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, source);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, messageType);
        if (messageId != null)
            activity.SetTag(CatgaActivitySource.Tags.MessageId, messageId);
    }

    public static Activity? StartPublishActivity(string system, string destination, string messageType, string? messageId = null)
    {
        var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        SetPublishTags(activity, system, destination, messageType, messageId);
        return activity;
    }
}
