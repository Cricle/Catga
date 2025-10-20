using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.DistributedId;
using Catga.Messages;

namespace Catga.Core;

/// <summary>Message helper methods (AOT-safe)</summary>
public static class MessageHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetOrGenerateMessageId<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        TRequest request,
        IDistributedIdGenerator idGenerator)
        where TRequest : class
    {
        if (request is IMessage message && message.MessageId != 0)
            return message.MessageId;
        return idGenerator.NextId();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetMessageType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>()
        => TypeNameCache<TRequest>.FullName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long? GetCorrelationId<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request)
        where TRequest : class
        => request is IMessage message ? message.CorrelationId : null;
}
