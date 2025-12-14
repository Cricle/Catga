namespace Catga.Persistence;

/// <summary>
/// Helper methods for building persistence keys consistently across stores.
/// </summary>
public static class PersistenceKeyHelper
{
    /// <summary>
    /// Build a key with prefix.
    /// </summary>
    public static string BuildKey(string prefix, string id) => prefix + id;

    /// <summary>
    /// Build a key for wait conditions.
    /// </summary>
    public static string WaitKey(string prefix, string correlationId) => $"{prefix}wait:{correlationId}";

    /// <summary>
    /// Build a key for ForEach progress.
    /// </summary>
    public static string ForEachKey(string flowId, int stepIndex) => $"{flowId}:foreach:{stepIndex}";

    /// <summary>
    /// Build a key for ForEach progress with prefix.
    /// </summary>
    public static string ForEachKey(string prefix, string flowId, int stepIndex) => $"{prefix}foreach:{flowId}:{stepIndex}";

    /// <summary>
    /// Encode a key for NATS KV (replace special characters).
    /// NATS KV keys cannot contain '.', ':', or '/'.
    /// </summary>
    public static string EncodeNatsKey(string id)
        => id.Replace(":", "_C_").Replace("/", "_S_").Replace(".", "_D_");

    /// <summary>
    /// Decode a NATS KV key back to original form.
    /// </summary>
    public static string DecodeNatsKey(string encoded)
        => encoded.Replace("_C_", ":").Replace("_S_", "/").Replace("_D_", ".");

    /// <summary>
    /// Encode a ForEach key for NATS KV.
    /// </summary>
    public static string EncodeNatsForEachKey(string flowId, int stepIndex)
        => EncodeNatsKey($"{flowId}:foreach:{stepIndex}");
}
