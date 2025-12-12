namespace Catga.Core;

/// <summary>
/// High-performance ID generation utilities using stack allocation.
/// </summary>
public static class IdGenerator
{
    /// <summary>
    /// Generate a Base64-encoded GUID string using stack allocation.
    /// </summary>
    public static string NewBase64Id()
    {
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer);
    }

    /// <summary>
    /// Generate a Base64-encoded GUID string without padding.
    /// </summary>
    public static string NewBase64IdNoPadding()
    {
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=');
    }
}
