using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.DistributedId;
using Catga.Messages;

namespace Catga.Core;

/// <summary>Message helper methods (AOT-friendly)</summary>
public static class MessageHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetOrGenerateMessageId<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, IDistributedIdGenerator idGenerator) where TRequest : class
    {
        if (request is IMessage message && !string.IsNullOrEmpty(message.MessageId))
            return message.MessageId;
        return idGenerator.NextId().ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetMessageType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>()
        => TypeNameCache<TRequest>.FullName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? GetCorrelationId<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request) where TRequest : class
        => request is IMessage message ? message.CorrelationId : null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateMessageId(string? messageId, string paramName = "messageId")
    {
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("MessageId is required", paramName);
    }
}
