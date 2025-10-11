using System.Collections.Concurrent;
using System.Text.Json;

namespace Catga.Scheduling;

/// <summary>
/// In-memory message scheduler implementation
/// Simple implementation for testing and single-instance scenarios
/// </summary>
public sealed class MemoryMessageScheduler : IMessageScheduler, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ScheduledMessage> _scheduledMessages = new();
    private readonly ICatgaMediator _mediator;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _backgroundTask;

    public MemoryMessageScheduler(ICatgaMediator mediator)
    {
        _mediator = mediator;
        _backgroundTask = ProcessScheduledMessagesAsync(_disposeCts.Token);
    }

    public ValueTask<ScheduledToken> ScheduleAsync<TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        var scheduledTime = DateTimeOffset.UtcNow.Add(delay);
        return ScheduleAtAsync(message, scheduledTime, cancellationToken);
    }

    public ValueTask<ScheduledToken> ScheduleAtAsync<TMessage>(
        TMessage message,
        DateTimeOffset scheduledTime,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        var id = Guid.NewGuid().ToString("N");
        var messageType = typeof(TMessage).AssemblyQualifiedName ?? typeof(TMessage).FullName ?? typeof(TMessage).Name;
        var messageData = JsonSerializer.SerializeToUtf8Bytes(message);

        var scheduledMessage = new ScheduledMessage
        {
            Id = id,
            MessageType = messageType,
            MessageData = messageData,
            ScheduledTime = scheduledTime,
            Status = ScheduleStatus.Pending
        };

        _scheduledMessages.TryAdd(id, scheduledMessage);

        return ValueTask.FromResult(new ScheduledToken(id));
    }

    public ValueTask<ScheduledToken> ScheduleRecurringAsync<TMessage>(
        TMessage message,
        string cronExpression,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        // Simplified: Schedule first execution 1 minute from now
        // Full implementation would use Cronos library for proper cron parsing
        var scheduledTime = DateTimeOffset.UtcNow.AddMinutes(1);
        
        var id = Guid.NewGuid().ToString("N");
        var messageType = typeof(TMessage).AssemblyQualifiedName ?? typeof(TMessage).FullName ?? typeof(TMessage).Name;
        var messageData = JsonSerializer.SerializeToUtf8Bytes(message);

        var scheduledMessage = new ScheduledMessage
        {
            Id = id,
            MessageType = messageType,
            MessageData = messageData,
            ScheduledTime = scheduledTime,
            CronExpression = cronExpression,
            Status = ScheduleStatus.Pending
        };

        _scheduledMessages.TryAdd(id, scheduledMessage);

        return ValueTask.FromResult(new ScheduledToken(id));
    }

    public ValueTask<bool> CancelAsync(
        ScheduledToken token,
        CancellationToken cancellationToken = default)
    {
        if (_scheduledMessages.TryGetValue(token.Id, out var message))
        {
            var updated = message with { Status = ScheduleStatus.Cancelled };
            return ValueTask.FromResult(_scheduledMessages.TryUpdate(token.Id, updated, message));
        }

        return ValueTask.FromResult(false);
    }

    private async Task ProcessScheduledMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var kvp in _scheduledMessages)
                {
                    var message = kvp.Value;

                    // Skip if not pending or not due yet
                    if (message.Status != ScheduleStatus.Pending || message.ScheduledTime > now)
                    {
                        continue;
                    }

                    // Try to mark as sent (CAS operation)
                    var updated = message with { Status = ScheduleStatus.Sent };
                    if (!_scheduledMessages.TryUpdate(kvp.Key, updated, message))
                    {
                        continue; // Another thread processed it
                    }

                    // Send the message
                    try
                    {
                        var messageType = Type.GetType(message.MessageType);
                        if (messageType == null)
                        {
                            // Mark as failed
                            _scheduledMessages.TryUpdate(kvp.Key, message with { Status = ScheduleStatus.Failed }, updated);
                            continue;
                        }

                        var deserializedMessage = JsonSerializer.Deserialize(message.MessageData, messageType);
                        if (deserializedMessage == null)
                        {
                            _scheduledMessages.TryUpdate(kvp.Key, message with { Status = ScheduleStatus.Failed }, updated);
                            continue;
                        }

                        // Send via mediator
                        await _mediator.SendAsync((dynamic)deserializedMessage, cancellationToken);

                        // Handle recurring messages
                        if (!string.IsNullOrEmpty(message.CronExpression))
                        {
                            // Simplified: reschedule 1 minute later
                            var nextScheduledTime = now.AddMinutes(1);
                            var recurring = message with 
                            { 
                                Id = Guid.NewGuid().ToString("N"),
                                ScheduledTime = nextScheduledTime,
                                Status = ScheduleStatus.Pending 
                            };
                            _scheduledMessages.TryAdd(recurring.Id, recurring);
                        }
                    }
                    catch
                    {
                        // Mark as failed
                        _scheduledMessages.TryUpdate(kvp.Key, message with { Status = ScheduleStatus.Failed }, updated);
                    }
                }

                // Wait before next check
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();

        try
        {
            await _backgroundTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _disposeCts.Dispose();
    }
}

