using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.DistributedId;
using Catga.Messages;

namespace Catga.Core;

/// <summary>Message helper methods (AOT-friendly)</summary>
public static class MessageHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetOrGenerateMessageId<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, IDistributedIdGenerator idGenerator) where TRequest : class
    {
        if (request is IMessage message && message.MessageId != 0)
            return message.MessageId;
        return idGenerator.NextId();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetMessageType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>()
        => TypeNameCache<TRequest>.FullName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long? GetCorrelationId<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request) where TRequest : class
        => request is IMessage message ? message.CorrelationId : null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessageId(long messageId, string paramName = "messageId")
    {
        if (messageId == 0)
            throw new ArgumentException("MessageId must be > 0", paramName);
    }
}
