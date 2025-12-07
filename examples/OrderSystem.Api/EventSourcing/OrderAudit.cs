using Catga.EventSourcing;

namespace OrderSystem.Api.EventSourcing;

/// <summary>
/// Order audit service for compliance and GDPR.
/// Demonstrates audit and compliance features.
/// </summary>
public class OrderAuditService
{
    private readonly IAuditLogStore _auditStore;
    private readonly ImmutabilityVerifier _verifier;
    private readonly GdprService _gdprService;

    public OrderAuditService(
        IAuditLogStore auditStore,
        IEventStore eventStore,
        IGdprStore gdprStore)
    {
        _auditStore = auditStore;
        _verifier = new ImmutabilityVerifier(eventStore);
        _gdprService = new GdprService(gdprStore);
    }

    /// <summary>Log an audit event.</summary>
    public async ValueTask LogAsync(string streamId, AuditAction action, string userId, string? details = null)
    {
        await _auditStore.LogAsync(new AuditLogEntry
        {
            StreamId = streamId,
            Action = action,
            UserId = userId,
            Details = details ?? ""
        });
    }

    /// <summary>Verify stream integrity.</summary>
    public async ValueTask<VerificationResult> VerifyStreamAsync(string streamId)
    {
        return await _verifier.VerifyStreamAsync(streamId);
    }

    /// <summary>Get audit logs for a stream.</summary>
    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsAsync(string streamId)
    {
        return await _auditStore.GetLogsAsync(streamId);
    }

    /// <summary>Request GDPR data erasure for a customer.</summary>
    public async ValueTask RequestCustomerErasureAsync(string customerId, string requestedBy)
    {
        await _gdprService.RequestErasureAsync(customerId, requestedBy);
    }

    /// <summary>Get pending GDPR erasure requests.</summary>
    public async ValueTask<IReadOnlyList<ErasureRequest>> GetPendingErasureRequestsAsync()
    {
        return await _gdprService.GetPendingRequestsAsync();
    }
}
