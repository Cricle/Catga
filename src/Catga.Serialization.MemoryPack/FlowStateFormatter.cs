using System.Runtime.CompilerServices;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Outbox;
using Catga.Inbox;
using Catga.DeadLetter;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// Custom MemoryPack formatter for FlowState.
/// Registers automatically via module initializer.
/// </summary>
public sealed class FlowStateFormatter : MemoryPackFormatter<FlowState>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref FlowState? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(9);
        writer.WriteString(value.Id);
        writer.WriteString(value.Type);
        writer.WriteUnmanaged((byte)value.Status);
        writer.WriteUnmanaged(value.Step);
        writer.WriteUnmanaged(value.Version);
        writer.WriteString(value.Owner);
        writer.WriteUnmanaged(value.HeartbeatAt);
        writer.WriteArray(value.Data);
        writer.WriteString(value.Error);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref FlowState? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        var id = reader.ReadString()!;
        var type = reader.ReadString()!;
        reader.ReadUnmanaged(out byte status);
        reader.ReadUnmanaged(out int step);
        reader.ReadUnmanaged(out long version);
        var owner = reader.ReadString();
        reader.ReadUnmanaged(out long heartbeatAt);
        var data = reader.ReadArray<byte>();
        var error = reader.ReadString();

        value = new FlowState
        {
            Id = id,
            Type = type,
            Status = (FlowStatus)status,
            Step = step,
            Version = version,
            Owner = owner,
            HeartbeatAt = heartbeatAt,
            Data = data is { Length: > 0 } ? data : null,
            Error = error
        };
    }
}

/// <summary>
/// Registration helper for MemoryPack formatters.
/// </summary>
public static class CatgaMemoryPackFormatters
{
    private static bool _registered;

    /// <summary>
    /// Register all Catga MemoryPack formatters. Call this once at application startup.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        MemoryPackFormatterProvider.Register(new FlowStateFormatter());
        MemoryPackFormatterProvider.Register(new OutboxMessageFormatter());
        MemoryPackFormatterProvider.Register(new InboxMessageFormatter());
        MemoryPackFormatterProvider.Register(new DeadLetterMessageFormatter());
        // Note: WaitCondition, ForEachProgress, FlowCompletedEventData, StoredSnapshotMetadata, StoredSnapshot<T>
        // are now marked with [MemoryPackable] and don't need custom formatters
    }

    /// <summary>
    /// Module initializer to automatically register formatters when the assembly is loaded.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => Register();
}

public sealed class OutboxMessageFormatter : MemoryPackFormatter<OutboxMessage>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref OutboxMessage? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(13);
        writer.WriteUnmanaged(value.MessageId);
        writer.WriteString(value.MessageType);
        writer.WriteArray(value.Payload);
        writer.WriteUnmanaged(value.CreatedAt);
        writer.WriteUnmanaged(value.PublishedAt.HasValue);
        writer.WriteUnmanaged(value.PublishedAt.GetValueOrDefault());
        writer.WriteUnmanaged(value.RetryCount);
        writer.WriteUnmanaged(value.MaxRetries);
        writer.WriteString(value.LastError);
        writer.WriteUnmanaged((byte)value.Status);
        writer.WriteUnmanaged(value.CorrelationId.HasValue);
        writer.WriteUnmanaged(value.CorrelationId.GetValueOrDefault());
        writer.WriteString(value.Metadata);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref OutboxMessage? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out long messageId);
        var messageType = reader.ReadString()!;
        var payload = reader.ReadArray<byte>()!;
        reader.ReadUnmanaged(out DateTime createdAt);
        reader.ReadUnmanaged(out bool hasPublishedAt);
        reader.ReadUnmanaged(out DateTime publishedAtValue);
        reader.ReadUnmanaged(out int retryCount);
        reader.ReadUnmanaged(out int maxRetries);
        var lastError = reader.ReadString();
        reader.ReadUnmanaged(out byte statusByte);
        reader.ReadUnmanaged(out bool hasCorrelationId);
        reader.ReadUnmanaged(out long correlationIdValue);
        var metadata = reader.ReadString();

        value = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = messageType,
            Payload = payload,
            CreatedAt = createdAt,
            PublishedAt = hasPublishedAt ? publishedAtValue : null,
            RetryCount = retryCount,
            MaxRetries = maxRetries,
            LastError = lastError,
            Status = (OutboxStatus)statusByte,
            CorrelationId = hasCorrelationId ? correlationIdValue : null,
            Metadata = metadata
        };
    }
}

public sealed class InboxMessageFormatter : MemoryPackFormatter<InboxMessage>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref InboxMessage? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(13);
        writer.WriteUnmanaged(value.MessageId);
        writer.WriteString(value.MessageType);
        writer.WriteArray(value.Payload);
        writer.WriteUnmanaged(value.ReceivedAt);
        writer.WriteUnmanaged(value.ProcessedAt.HasValue);
        writer.WriteUnmanaged(value.ProcessedAt.GetValueOrDefault());
        writer.WriteArray(value.ProcessingResult);
        writer.WriteUnmanaged((byte)value.Status);
        writer.WriteUnmanaged(value.LockExpiresAt.HasValue);
        writer.WriteUnmanaged(value.LockExpiresAt.GetValueOrDefault());
        writer.WriteUnmanaged(value.CorrelationId.HasValue);
        writer.WriteUnmanaged(value.CorrelationId.GetValueOrDefault());
        writer.WriteString(value.Metadata);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref InboxMessage? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out long messageId);
        var messageType = reader.ReadString()!;
        var payload = reader.ReadArray<byte>()!;
        reader.ReadUnmanaged(out DateTime receivedAt);
        reader.ReadUnmanaged(out bool hasProcessedAt);
        reader.ReadUnmanaged(out DateTime processedAtValue);
        var processingResult = reader.ReadArray<byte>();
        reader.ReadUnmanaged(out byte statusByte);
        reader.ReadUnmanaged(out bool hasLockExpiresAt);
        reader.ReadUnmanaged(out DateTime lockExpiresAtValue);
        reader.ReadUnmanaged(out bool hasCorrelationId);
        reader.ReadUnmanaged(out long correlationIdValue);
        var metadata = reader.ReadString();

        value = new InboxMessage
        {
            MessageId = messageId,
            MessageType = messageType,
            Payload = payload,
            ReceivedAt = receivedAt,
            ProcessedAt = hasProcessedAt ? processedAtValue : null,
            ProcessingResult = processingResult,
            Status = (InboxStatus)statusByte,
            LockExpiresAt = hasLockExpiresAt ? lockExpiresAtValue : null,
            CorrelationId = hasCorrelationId ? correlationIdValue : null,
            Metadata = metadata
        };
    }
}

public sealed class DeadLetterMessageFormatter : MemoryPackFormatter<DeadLetterMessage>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref DeadLetterMessage value)
    {
        writer.WriteObjectHeader(8);
        writer.WriteUnmanaged(value.MessageId);
        writer.WriteString(value.MessageType);
        writer.WriteArray(value.Message);
        writer.WriteString(value.ExceptionType);
        writer.WriteString(value.ExceptionMessage);
        writer.WriteString(value.StackTrace);
        writer.WriteUnmanaged(value.RetryCount);
        writer.WriteUnmanaged(value.FailedAt);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref DeadLetterMessage value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = default;
            return;
        }

        reader.ReadUnmanaged(out long messageId);
        var messageType = reader.ReadString()!;
        var messageData = reader.ReadArray<byte>()!;
        var exceptionType = reader.ReadString()!;
        var exceptionMessage = reader.ReadString()!;
        var stackTrace = reader.ReadString()!;
        reader.ReadUnmanaged(out int retryCount);
        reader.ReadUnmanaged(out DateTime failedAt);

        value = new DeadLetterMessage
        {
            MessageId = messageId,
            MessageType = messageType,
            Message = messageData,
            ExceptionType = exceptionType,
            ExceptionMessage = exceptionMessage,
            StackTrace = stackTrace,
            RetryCount = retryCount,
            FailedAt = failedAt
        };
    }
}
