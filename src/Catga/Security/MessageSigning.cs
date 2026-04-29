using System.Security.Cryptography;
using System.Text;

namespace Catga.Security;

/// <summary>
/// Signs and verifies message payloads to prevent tampering.
/// Pure .NET — no external dependencies.
/// </summary>
public interface IMessageSigner
{
    /// <summary>Sign a message payload. Returns the signature.</summary>
    string Sign(byte[] payload);

    /// <summary>Verify a message payload against its signature.</summary>
    bool Verify(byte[] payload, string signature);
}

/// <summary>
/// HMAC-SHA256 message signer.
/// Use a shared secret key across all nodes.
/// </summary>
public sealed class HmacMessageSigner : IMessageSigner
{
    private readonly byte[] _key;

    public HmacMessageSigner(string secretKey)
        => _key = Encoding.UTF8.GetBytes(secretKey);

    public HmacMessageSigner(byte[] secretKey)
        => _key = secretKey;

    public string Sign(byte[] payload)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(payload);
        return Convert.ToBase64String(hash);
    }

    public bool Verify(byte[] payload, string signature)
    {
        var expected = Sign(payload);
        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}

/// <summary>
/// Options for message signing.
/// </summary>
public sealed class MessageSigningOptions
{
    /// <summary>Secret key for HMAC signing.</summary>
    public required string SecretKey { get; init; }

    /// <summary>Header name for the signature. Default: x-catga-sig</summary>
    public string SignatureHeader { get; init; } = "x-catga-sig";

    /// <summary>Whether to reject messages with invalid/missing signatures.</summary>
    public bool RejectInvalid { get; init; } = true;
}
