using System.Security.Cryptography;
using System.Text;

namespace Catga.EventSourcing;

#region Immutability Verification

/// <summary>
/// Result of immutability verification.
/// </summary>
public sealed class VerificationResult
{
    public bool IsValid { get; init; }
    public string Hash { get; init; } = "";
    public string? Error { get; init; }

    public static VerificationResult Valid(string hash) => new() { IsValid = true, Hash = hash };
    public static VerificationResult Invalid(string error, string hash = "") => new() { IsValid = false, Error = error, Hash = hash };
}

/// <summary>
/// Verifies event stream immutability using cryptographic hashing.
/// </summary>
public sealed class ImmutabilityVerifier
{
    private readonly IEventStore _eventStore;

    public ImmutabilityVerifier(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    /// <summary>Verify stream and return its hash.</summary>
    public async ValueTask<VerificationResult> VerifyStreamAsync(string streamId, CancellationToken ct = default)
    {
        var stream = await _eventStore.ReadAsync(streamId, cancellationToken: ct);
        if (stream.Events.Count == 0)
            return VerificationResult.Invalid("Stream is empty or does not exist");

        var hash = ComputeStreamHash(stream);
        return VerificationResult.Valid(hash);
    }

    /// <summary>Verify stream against expected hash.</summary>
    public async ValueTask<VerificationResult> VerifyAgainstHashAsync(string streamId, string expectedHash, CancellationToken ct = default)
    {
        var result = await VerifyStreamAsync(streamId, ct);
        if (!result.IsValid) return result;

        if (result.Hash != expectedHash)
            return VerificationResult.Invalid($"Hash mismatch: expected {expectedHash}, got {result.Hash}", result.Hash);

        return result;
    }

    private static string ComputeStreamHash(EventStream stream)
    {
        using var sha256 = SHA256.Create();
        var sb = new StringBuilder();

        foreach (var evt in stream.Events)
        {
            sb.Append(evt.Version);
            sb.Append('|');
            sb.Append(evt.EventType);
            sb.Append('|');
            sb.Append(evt.Timestamp.Ticks);
            sb.Append('|');
            sb.Append(evt.Event.GetHashCode());
            sb.Append(';');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }
}

#endregion

#region Audit Logging

/// <summary>
/// Audit action types.
/// </summary>
public enum AuditAction
{
    EventAppended,
    StreamRead,
    SnapshotCreated,
    SnapshotLoaded,
    StreamDeleted,
    DataErased
}

/// <summary>
/// Audit log entry.
/// </summary>
public sealed class AuditLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public AuditAction Action { get; init; }
    public string StreamId { get; init; } = "";
    public string UserId { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Details { get; init; } = "";
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Audit log store interface.
/// </summary>
public interface IAuditLogStore
{
    ValueTask LogAsync(AuditLogEntry entry, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsAsync(string streamId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByTimeRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByUserAsync(string userId, CancellationToken ct = default);
}

#endregion

#region GDPR Support

/// <summary>
/// Erasure request status.
/// </summary>
public enum ErasureStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// GDPR erasure request.
/// </summary>
public sealed class ErasureRequest
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string SubjectId { get; init; } = "";
    public string RequestedBy { get; init; } = "";
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ErasureStatus Status { get; set; } = ErasureStatus.Pending;
    public List<string> AffectedStreams { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// GDPR erasure store interface.
/// </summary>
public interface IGdprStore
{
    ValueTask SaveRequestAsync(ErasureRequest request, CancellationToken ct = default);
    ValueTask<ErasureRequest?> GetErasureRequestAsync(string subjectId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ErasureRequest>> GetPendingRequestsAsync(CancellationToken ct = default);
}

/// <summary>
/// GDPR compliance service.
/// </summary>
public sealed class GdprService
{
    private readonly IGdprStore _store;

    public GdprService(IGdprStore store)
    {
        _store = store;
    }

    /// <summary>Request data erasure for a subject.</summary>
    public async ValueTask RequestErasureAsync(string subjectId, string requestedBy, CancellationToken ct = default)
    {
        var request = new ErasureRequest
        {
            SubjectId = subjectId,
            RequestedBy = requestedBy,
            Status = ErasureStatus.Pending
        };
        await _store.SaveRequestAsync(request, ct);
    }

    /// <summary>Mark erasure as completed.</summary>
    public async ValueTask CompleteErasureAsync(string subjectId, IEnumerable<string> affectedStreams, CancellationToken ct = default)
    {
        var request = await _store.GetErasureRequestAsync(subjectId, ct);
        if (request == null) return;

        request.Status = ErasureStatus.Completed;
        request.CompletedAt = DateTime.UtcNow;
        request.AffectedStreams = affectedStreams.ToList();
        await _store.SaveRequestAsync(request, ct);
    }

    /// <summary>Get all pending erasure requests.</summary>
    public ValueTask<IReadOnlyList<ErasureRequest>> GetPendingRequestsAsync(CancellationToken ct = default)
        => _store.GetPendingRequestsAsync(ct);
}

#endregion

#region Crypto Erasure

/// <summary>
/// Encryption key store interface for crypto erasure.
/// </summary>
public interface IEncryptionKeyStore
{
    ValueTask SaveKeyAsync(string subjectId, byte[] key, CancellationToken ct = default);
    ValueTask<byte[]?> GetKeyAsync(string subjectId, CancellationToken ct = default);
    ValueTask DeleteKeyAsync(string subjectId, CancellationToken ct = default);
}

/// <summary>
/// Crypto erasure service - destroys encryption keys to make data unreadable.
/// </summary>
public sealed class CryptoErasureService
{
    private readonly IEncryptionKeyStore _keyStore;

    public CryptoErasureService(IEncryptionKeyStore keyStore)
    {
        _keyStore = keyStore;
    }

    /// <summary>Get or create encryption key for subject.</summary>
    public async ValueTask<byte[]> GetOrCreateKeyAsync(string subjectId, CancellationToken ct = default)
    {
        var existing = await _keyStore.GetKeyAsync(subjectId, ct);
        if (existing != null) return existing;

        var key = new byte[32]; // 256-bit key
        RandomNumberGenerator.Fill(key);
        await _keyStore.SaveKeyAsync(subjectId, key, ct);
        return key;
    }

    /// <summary>Destroy encryption key (crypto erasure).</summary>
    public ValueTask DestroyKeyAsync(string subjectId, CancellationToken ct = default)
        => _keyStore.DeleteKeyAsync(subjectId, ct);
}

#endregion
