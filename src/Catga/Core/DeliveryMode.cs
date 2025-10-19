namespace Catga.Core;

/// <summary>Message delivery mode</summary>
public enum DeliveryMode
{
    /// <summary>Sync wait for ACK (payment, order)</summary>
    WaitForResult = 0,

    /// <summary>Async retry with persistent queue (notification, sync)</summary>
    AsyncRetry = 1
}

