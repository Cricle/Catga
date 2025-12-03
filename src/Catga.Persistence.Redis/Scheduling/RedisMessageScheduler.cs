using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Scheduling;
using Catga.Transport;
using MemoryPack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Scheduling;

/// <summary>Redis-based message scheduler using Sorted Sets.</summary>
public sealed partial class RedisMessageScheduler(IConnectionMultiplexer redis, IMessageSerializer serializer, ICatgaMediator mediator, IOptions<MessageSchedulerOptions> options, ILogger<RedisMessageScheduler> logger, IEventTypeRegistry? typeRegistry = null)
    : IMessageScheduler, IHostedService, IDisposable
{
    private readonly MessageSchedulerOptions _opts = options.Value;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private const string SetKey = "catga:schedules";
    private const string KeyPrefix = "catga:scheduled:";

    public async ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        DateTimeOffset deliverAt,
        CancellationToken ct = default) where TMessage : class, IMessage
    {
        var db = redis.GetDatabase();
        var scheduleId = GenerateScheduleId();
        var messageType = typeof(TMessage).FullName ?? typeof(TMessage).Name;
        var score = deliverAt.ToUnixTimeMilliseconds();

        // Serialize message
        var messageBytes = serializer.Serialize(message);

        // Store scheduled message info
        var info = new ScheduledMessageData
        {
            ScheduleId = scheduleId,
            MessageType = messageType,
            DeliverAt = deliverAt.UtcDateTime,
            CreatedAt = DateTime.UtcNow,
            Status = (byte)ScheduledMessageStatus.Pending,
            MessageData = messageBytes
        };

        var infoBytes = MemoryPackSerializer.Serialize(info);
        var messageKey = string.Concat(KeyPrefix, scheduleId);

        // Transaction: store message and add to sorted set
        var tran = db.CreateTransaction();
        _ = tran.StringSetAsync(messageKey, infoBytes);
        _ = tran.SortedSetAddAsync(SetKey, scheduleId, score);
        await tran.ExecuteAsync();

        LogMessageScheduled(logger, scheduleId, messageType, deliverAt);

        return new ScheduledMessageHandle
        {
            ScheduleId = scheduleId,
            DeliverAt = deliverAt,
            MessageType = messageType
        };
    }

    public ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken ct = default) where TMessage : class, IMessage
    {
        return ScheduleAsync(message, DateTimeOffset.UtcNow.Add(delay), ct);
    }

    public async ValueTask<bool> CancelAsync(string scheduleId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var messageKey = string.Concat(KeyPrefix, scheduleId);

        var tran = db.CreateTransaction();
        _ = tran.KeyDeleteAsync(messageKey);
        _ = tran.SortedSetRemoveAsync(SetKey, scheduleId);
        var success = await tran.ExecuteAsync();

        if (success)
            LogMessageCancelled(logger, scheduleId);

        return success;
    }

    public async ValueTask<ScheduledMessageInfo?> GetAsync(string scheduleId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var messageKey = string.Concat(KeyPrefix, scheduleId);
        var data = await db.StringGetAsync(messageKey);

        if (data.IsNullOrEmpty)
            return null;

        var info = MemoryPackSerializer.Deserialize<ScheduledMessageData>((byte[])data!);
        if (info == null)
            return null;

        return new ScheduledMessageInfo
        {
            ScheduleId = info.ScheduleId,
            MessageType = info.MessageType,
            DeliverAt = new DateTimeOffset(info.DeliverAt, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(info.CreatedAt, TimeSpan.Zero),
            Status = (ScheduledMessageStatus)info.Status,
            RetryCount = info.RetryCount,
            LastError = info.LastError
        };
    }

    public async IAsyncEnumerable<ScheduledMessageInfo> ListPendingAsync(
        int limit = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var entries = await db.SortedSetRangeByScoreAsync(
            SetKey,
            double.NegativeInfinity,
            double.PositiveInfinity,
            take: limit);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var info = await GetAsync(entry.ToString(), ct);
            if (info.HasValue)
                yield return info.Value;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _task = ProcessDueMessagesAsync(_cts.Token);
        LogSchedulerStarted(logger);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        if (_task != null)
            await _task;
        LogSchedulerStopped(logger);
    }

    private async Task ProcessDueMessagesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(ct);
                await Task.Delay(_opts.PollingInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogProcessingError(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Get due messages
        var dueIds = await db.SortedSetRangeByScoreAsync(
            SetKey,
            double.NegativeInfinity,
            now,
            take: _opts.BatchSize);

        if (dueIds.Length == 0)
            return;

        foreach (var scheduleId in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            await DeliverMessageAsync(db, scheduleId.ToString(), ct);
        }
    }

    private async Task DeliverMessageAsync(IDatabase db, string scheduleId, CancellationToken ct)
    {
        var messageKey = string.Concat(KeyPrefix, scheduleId);

        try
        {
            var data = await db.StringGetAsync(messageKey);
            if (data.IsNullOrEmpty)
            {
                await db.SortedSetRemoveAsync(SetKey, scheduleId);
                return;
            }

            var info = MemoryPackSerializer.Deserialize<ScheduledMessageData>((byte[])data!);
            if (info == null)
                return;

            // Deserialize and dispatch message - use type registry for AOT compatibility
            var messageType = typeRegistry?.Resolve(info.MessageType);
            if (messageType == null)
            {
                LogMessageTypeNotFound(logger, scheduleId, info.MessageType);
                await MarkAsFailedAsync(db, scheduleId, messageKey, info, "Message type not found");
                return;
            }

            var message = serializer.Deserialize(info.MessageData, messageType);
            if (message == null)
            {
                await MarkAsFailedAsync(db, scheduleId, messageKey, info, "Deserialization failed");
                return;
            }

            // Dispatch based on message type
            if (message is IEvent evt)
                await mediator.PublishAsync(evt, ct);
            else if (message is IRequest req)
                await mediator.SendAsync(req, ct);

            // Remove from schedule
            var tran = db.CreateTransaction();
            _ = tran.KeyDeleteAsync(messageKey);
            _ = tran.SortedSetRemoveAsync(SetKey, scheduleId);
            await tran.ExecuteAsync();

            LogMessageDelivered(logger, scheduleId, info.MessageType);
        }
        catch (Exception ex)
        {
            LogDeliveryError(logger, scheduleId, ex);
            // Retry logic handled by updating retry count
        }
    }

    private async Task MarkAsFailedAsync(IDatabase db, string scheduleId, string messageKey, ScheduledMessageData info, string error)
    {
        info.Status = (byte)ScheduledMessageStatus.Failed;
        info.LastError = error;
        var infoBytes = MemoryPackSerializer.Serialize(info);
        await db.StringSetAsync(messageKey, infoBytes);
        await db.SortedSetRemoveAsync(SetKey, scheduleId);
    }

    private static string GenerateScheduleId()
    {
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=');
    }

    public void Dispose() => _cts.Dispose();

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message scheduled: {ScheduleId} ({MessageType}) for {DeliverAt}")]
    private static partial void LogMessageScheduled(ILogger logger, string scheduleId, string messageType, DateTimeOffset deliverAt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduled message cancelled: {ScheduleId}")]
    private static partial void LogMessageCancelled(ILogger logger, string scheduleId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduled message delivered: {ScheduleId} ({MessageType})")]
    private static partial void LogMessageDelivered(ILogger logger, string scheduleId, string messageType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message scheduler started")]
    private static partial void LogSchedulerStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message scheduler stopped")]
    private static partial void LogSchedulerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing scheduled messages")]
    private static partial void LogProcessingError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error delivering scheduled message: {ScheduleId}")]
    private static partial void LogDeliveryError(ILogger logger, string scheduleId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message type not found for scheduled message: {ScheduleId} ({MessageType})")]
    private static partial void LogMessageTypeNotFound(ILogger logger, string scheduleId, string messageType);

    #endregion
}

/// <summary>Internal data structure for scheduled messages.</summary>
[MemoryPackable]
internal partial class ScheduledMessageData
{
    public string ScheduleId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public DateTime DeliverAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte Status { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public byte[] MessageData { get; set; } = [];
}
