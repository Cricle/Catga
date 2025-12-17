using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Transport;

/// <summary>
/// Factory for creating TransportContext instances.
/// </summary>
public static class TransportContextFactory
{
    /// <summary>
    /// Create a default TransportContext for the given message type.
    /// </summary>
    public static TransportContext CreateDefault<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>() where TMessage : class
        => new()
        {
            MessageId = MessageExtensions.NewMessageId(),
            MessageType = TypeNameCache<TMessage>.FullName,
            SentAt = DateTime.UtcNow
        };

    /// <summary>
    /// Get or create a TransportContext, using the provided one if not null.
    /// </summary>
    public static TransportContext GetOrCreate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TransportContext? context) where TMessage : class
        => context ?? CreateDefault<TMessage>();
}
