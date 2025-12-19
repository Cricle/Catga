namespace Catga.Core;

/// <summary>High-performance ID generation using stack allocation.</summary>
public static class IdGenerator
{
    public static string NewBase64Id()
    {
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer);
    }

    public static string NewBase64IdNoPadding()
    {
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=');
    }
}
