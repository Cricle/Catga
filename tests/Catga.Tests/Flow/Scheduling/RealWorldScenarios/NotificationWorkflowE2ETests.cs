using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling.RealWorldScenarios;

/// <summary>
/// E2E tests for notification and reminder workflow scenarios.
/// </summary>
public class NotificationWorkflowE2ETests
{
    #region Test Infrastructure

    public class NotificationState : IFlowState
    {
        public string? FlowId { get; set; }
        public string UserId { get; set; } = "USER-001";
        public string Email { get; set; } = "user@example.com";
        public string Phone { get; set; } = "+1234567890";
        public NotificationType Type { get; set; }
        public string Message { get; set; } = "";
        public int SendAttempts { get; set; }
        public bool Delivered { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public List<string> NotificationsSent { get; set; } = [];
    }

    public enum NotificationType { Email, SMS, Push, InApp }

    // Commands
    public record SendEmailCommand(string Email, string Subject, string Body) : IRequest<bool>;
    public record SendSmsCommand(string Phone, string Message) : IRequest<bool>;
    public record SendPushCommand(string UserId, string Title, string Body) : IRequest<bool>;
    public record CheckDeliveryCommand(string NotificationId) : IRequest<bool>;

    #endregion

    #region Scenario 1: Scheduled Daily Digest

    /// <summary>
    /// Scenario: Schedule daily digest email at 9 AM user's local time
    /// </summary>
    [Fact]
    public async Task DailyDigest_ShouldScheduleAtSpecificTime()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("digest-schedule");
            });

        var config = new DailyDigestFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<NotificationState, DailyDigestFlowConfig>(mediator, store, config, scheduler);

        // Schedule for tomorrow 9 AM
        var tomorrow9am = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var state = new NotificationState { ScheduledAt = tomorrow9am };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        capturedTime.UtcDateTime.Should().BeCloseTo(tomorrow9am, TimeSpan.FromSeconds(1));
    }

    private class DailyDigestFlowConfig : FlowConfig<NotificationState>
    {
        public override string FlowId => "daily-digest";

        protected override void Configure(IFlowBuilder<NotificationState> flow)
        {
            flow
                .ScheduleAt(s => s.ScheduledAt ?? DateTime.UtcNow)
                .Send<SendEmailCommand, bool>(s => new SendEmailCommand(
                    s.Email,
                    "Your Daily Digest",
                    "Here's what happened today..."));
        }
    }

    #endregion

    #region Scenario 2: Multi-Channel Notification with Fallback

    /// <summary>
    /// Scenario: Try Push -> Wait 5min -> If not read, send SMS -> Wait 1h -> Send Email
    /// </summary>
    [Fact]
    public async Task MultiChannelNotification_ShouldCascadeWithDelays()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduleCount = 0;
        var scheduledDelays = new List<TimeSpan>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var resumeAt = callInfo.ArgAt<DateTimeOffset>(2);
                scheduledDelays.Add(resumeAt - DateTimeOffset.UtcNow);
                return ValueTask.FromResult($"cascade-{++scheduleCount}");
            });

        // Push succeeds but not delivered
        mediator.SendAsync(Arg.Any<SendPushCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<bool>.Success(true));

        var config = new MultiChannelNotificationFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<NotificationState, MultiChannelNotificationFlowConfig>(mediator, store, config, scheduler);
        var state = new NotificationState
        {
            Message = "Important notification",
            Delivered = false
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduledDelays.Should().HaveCount(1);
        scheduledDelays[0].Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
    }

    private class MultiChannelNotificationFlowConfig : FlowConfig<NotificationState>
    {
        public override string FlowId => "multi-channel";

        protected override void Configure(IFlowBuilder<NotificationState> flow)
        {
            flow
                .Send<SendPushCommand, bool>(s => new SendPushCommand(s.UserId, "Alert", s.Message))
                .Delay(TimeSpan.FromMinutes(5))
                .If(s => !s.Delivered)
                    .Send<SendSmsCommand, bool>(s => new SendSmsCommand(s.Phone, s.Message))
                    .Delay(TimeSpan.FromHours(1))
                    .If(s => !s.Delivered)
                        .Send<SendEmailCommand, bool>(s => new SendEmailCommand(s.Email, "Alert", s.Message))
                    .EndIf()
                .EndIf();
        }
    }

    #endregion

    #region Scenario 3: Appointment Reminder Series

    /// <summary>
    /// Scenario: Send reminders at 7 days, 1 day, and 1 hour before appointment
    /// </summary>
    [Fact]
    public async Task AppointmentReminder_ShouldScheduleMultipleReminders()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduledTimes = new List<DateTimeOffset>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                scheduledTimes.Add(callInfo.ArgAt<DateTimeOffset>(2));
                return ValueTask.FromResult($"reminder-{scheduledTimes.Count}");
            });

        mediator.SendAsync(Arg.Any<SendEmailCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<bool>.Success(true));

        var config = new AppointmentReminderFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<NotificationState, AppointmentReminderFlowConfig>(mediator, store, config, scheduler);

        // Appointment in 8 days
        var appointmentTime = DateTime.UtcNow.AddDays(8);
        var state = new NotificationState { ScheduledAt = appointmentTime };

        // Act
        var result = await executor.RunAsync(state);

        // Assert - First reminder (7 days before) should be scheduled
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduledTimes.Should().HaveCount(1);

        var expectedTime = appointmentTime.AddDays(-7);
        scheduledTimes[0].UtcDateTime.Should().BeCloseTo(expectedTime, TimeSpan.FromSeconds(5));
    }

    private class AppointmentReminderFlowConfig : FlowConfig<NotificationState>
    {
        public override string FlowId => "appointment-reminder";

        protected override void Configure(IFlowBuilder<NotificationState> flow)
        {
            flow
                // 7 days before
                .ScheduleAt(s => s.ScheduledAt!.Value.AddDays(-7))
                .Send<SendEmailCommand, bool>(s => new SendEmailCommand(s.Email, "Reminder: Appointment in 7 days", s.Message))
                // 1 day before
                .ScheduleAt(s => s.ScheduledAt!.Value.AddDays(-1))
                .Send<SendEmailCommand, bool>(s => new SendEmailCommand(s.Email, "Reminder: Appointment tomorrow", s.Message))
                // 1 hour before
                .ScheduleAt(s => s.ScheduledAt!.Value.AddHours(-1))
                .Send<SendSmsCommand, bool>(s => new SendSmsCommand(s.Phone, "Your appointment is in 1 hour"));
        }
    }

    #endregion

    #region Scenario 4: Rate-Limited Notifications

    /// <summary>
    /// Scenario: Limit to 3 notifications per day - delay if exceeded
    /// </summary>
    [Fact]
    public async Task RateLimitedNotification_ShouldDelayIfLimitExceeded()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var wasDelayed = false;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                wasDelayed = true;
                return ValueTask.FromResult("rate-limit-delay");
            });

        var config = new RateLimitedNotificationFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<NotificationState, RateLimitedNotificationFlowConfig>(mediator, store, config, scheduler);

        // Already sent 3 notifications today
        var state = new NotificationState
        {
            NotificationsSent = ["notif-1", "notif-2", "notif-3"],
            Message = "4th notification"
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Should be delayed due to rate limit
        result.Status.Should().Be(DslFlowStatus.Suspended);
        wasDelayed.Should().BeTrue();
    }

    private class RateLimitedNotificationFlowConfig : FlowConfig<NotificationState>
    {
        public override string FlowId => "rate-limited";

        protected override void Configure(IFlowBuilder<NotificationState> flow)
        {
            flow
                .If(s => s.NotificationsSent.Count >= 3)
                    .Delay(TimeSpan.FromHours(24)) // Wait until tomorrow
                .EndIf()
                .Send<SendPushCommand, bool>(s => new SendPushCommand(s.UserId, "Notification", s.Message));
        }
    }

    #endregion

    #region Helper

    private static InMemoryTestStore CreateInMemoryStore() => new();

    private class InMemoryTestStore : IDslFlowStore
    {
        private readonly Dictionary<string, object> _flows = new();
        private readonly Dictionary<string, WaitCondition> _waitConditions = new();
        private readonly Dictionary<string, ForEachProgress> _forEachProgress = new();

        public Task<bool> CreateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<FlowSnapshot<TState>?> GetAsync<TState>(string flowId, CancellationToken ct = default) where TState : class, IFlowState
            => Task.FromResult(_flows.TryGetValue(flowId, out var s) ? (FlowSnapshot<TState>?)s : null);
        public Task<bool> UpdateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default) => Task.FromResult(_flows.Remove(flowId));
        public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(_waitConditions.TryGetValue(correlationId, out var c) ? c : null);
        public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
        { _waitConditions.Remove(correlationId); return Task.CompletedTask; }
        public Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WaitCondition>>(_waitConditions.Values.Where(c => DateTime.UtcNow - c.CreatedAt > c.Timeout).ToList());
        public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
        { _forEachProgress[$"{flowId}:{stepIndex}"] = progress; return Task.CompletedTask; }
        public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
            => Task.FromResult(_forEachProgress.TryGetValue($"{flowId}:{stepIndex}", out var p) ? p : null);
        public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        { _forEachProgress.Remove($"{flowId}:{stepIndex}"); return Task.CompletedTask; }
    }

    #endregion
}
