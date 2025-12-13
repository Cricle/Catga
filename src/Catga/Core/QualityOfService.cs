namespace Catga;

// ========== Quality of Service Levels ==========

/// <summary>Message Quality of Service (MQTT-like)</summary>
public enum QualityOfService
{
    /// <summary>QoS 0: Fire-and-forget (logs, metrics)</summary>
    AtMostOnce = 0,

    /// <summary>QoS 1: At least once, may duplicate (orders, sync)</summary>
    AtLeastOnce = 1,

    /// <summary>QoS 2: Exactly once, idempotent (payments, transactions)</summary>
    ExactlyOnce = 2
}

