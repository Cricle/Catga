namespace Catga.Abstractions;

/// <summary>
/// Extension methods for QualityOfService enum.
/// </summary>
public static class QoSExtensions
{
    /// <summary>
    /// Convert QoS to a tag-friendly string without boxing.
    /// </summary>
    public static string ToTagString(this QualityOfService qos) => qos switch
    {
        QualityOfService.AtMostOnce => "AtMostOnce",
        QualityOfService.AtLeastOnce => "AtLeastOnce",
        QualityOfService.ExactlyOnce => "ExactlyOnce",
        _ => "Unknown"
    };

    /// <summary>
    /// Check if QoS requires acknowledgment.
    /// </summary>
    public static bool RequiresAck(this QualityOfService qos)
        => qos is QualityOfService.AtLeastOnce or QualityOfService.ExactlyOnce;

    /// <summary>
    /// Check if QoS requires deduplication.
    /// </summary>
    public static bool RequiresDedup(this QualityOfService qos)
        => qos == QualityOfService.ExactlyOnce;
}
