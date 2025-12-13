using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Notification workflow scenarios with multi-channel delivery and fallback.
/// </summary>
public class NotificationFlowTests
{
    #region Test State

    public class NotificationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Message { get; set; } = "";
        public NotificationType Type { get; set; }
        public NotificationPriority Priority { get; set; }

        // Delivery status
        public bool EmailSent { get; set; }
        public bool SmsSent { get; set; }
        public bool PushSent { get; set; }
        public bool SlackSent { get; set; }

        // Results
        public List<string> DeliveredChannels { get; set; } = new();
        public List<string> FailedChannels { get; set; } = new();
        public int TotalAttempts { get; set; }
    }

    public enum NotificationType { Info, Warning, Critical, Marketing }
    public enum NotificationPriority { Low, Medium, High, Urgent }

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Notification_MultiChannel_SendsToAllChannels()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<NotificationState>("multi-channel")
            .Step("send-email", async (state, ct) =>
            {
                state.EmailSent = true;
                state.DeliveredChannels.Add("email");
                return true;
            })
            .Step("send-sms", async (state, ct) =>
            {
                state.SmsSent = true;
                state.DeliveredChannels.Add("sms");
                return true;
            })
            .Step("send-push", async (state, ct) =>
            {
                state.PushSent = true;
                state.DeliveredChannels.Add("push");
                return true;
            })
            .Build();

        var initialState = new NotificationState
        {
            FlowId = "notify-001",
            UserId = "USER-001",
            Message = "Test notification",
            Type = NotificationType.Info
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.DeliveredChannels.Should().HaveCount(3);
        result.State.DeliveredChannels.Should().Contain(new[] { "email", "sms", "push" });
    }

    [Fact]
    public async Task Notification_ByPriority_SelectsCorrectChannels()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<NotificationState>("priority-routing")
            .Switch(s => s.Priority)
                .Case(NotificationPriority.Urgent, f => f
                    .Step("urgent-all-channels", async (state, ct) =>
                    {
                        state.EmailSent = true;
                        state.SmsSent = true;
                        state.PushSent = true;
                        state.SlackSent = true;
                        state.DeliveredChannels.AddRange(new[] { "email", "sms", "push", "slack" });
                        return true;
                    }))
                .Case(NotificationPriority.High, f => f
                    .Step("high-email-push", async (state, ct) =>
                    {
                        state.EmailSent = true;
                        state.PushSent = true;
                        state.DeliveredChannels.AddRange(new[] { "email", "push" });
                        return true;
                    }))
                .Case(NotificationPriority.Medium, f => f
                    .Step("medium-email", async (state, ct) =>
                    {
                        state.EmailSent = true;
                        state.DeliveredChannels.Add("email");
                        return true;
                    }))
                .Default(f => f
                    .Step("low-push-only", async (state, ct) =>
                    {
                        state.PushSent = true;
                        state.DeliveredChannels.Add("push");
                        return true;
                    }))
            .EndSwitch()
            .Build();

        // Test Urgent
        var urgentState = new NotificationState { FlowId = "urgent", Priority = NotificationPriority.Urgent };
        var urgentResult = await executor.ExecuteAsync(flow, urgentState);
        urgentResult.State.DeliveredChannels.Should().HaveCount(4);

        // Test High
        var highState = new NotificationState { FlowId = "high", Priority = NotificationPriority.High };
        var highResult = await executor.ExecuteAsync(flow, highState);
        highResult.State.DeliveredChannels.Should().HaveCount(2);

        // Test Low
        var lowState = new NotificationState { FlowId = "low", Priority = NotificationPriority.Low };
        var lowResult = await executor.ExecuteAsync(flow, lowState);
        lowResult.State.DeliveredChannels.Should().HaveCount(1);
        lowResult.State.DeliveredChannels.Should().Contain("push");
    }

    [Fact]
    public async Task Notification_WithFallback_TriesAlternativeOnFailure()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var emailAttempts = 0;

        var flow = FlowBuilder.Create<NotificationState>("fallback-notification")
            .Step("try-email", async (state, ct) =>
            {
                emailAttempts++;
                state.TotalAttempts++;
                // Email always fails
                state.FailedChannels.Add("email");
                return false; // Return false to indicate failure without exception
            })
            .If(state => !state.EmailSent)
                .Then(f => f.Step("fallback-sms", async (state, ct) =>
                {
                    state.TotalAttempts++;
                    state.SmsSent = true;
                    state.DeliveredChannels.Add("sms");
                    return true;
                }))
            .EndIf()
            .Build();

        var initialState = new NotificationState { FlowId = "fallback-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.EmailSent.Should().BeFalse();
        result.State.SmsSent.Should().BeTrue();
        result.State.FailedChannels.Should().Contain("email");
        result.State.DeliveredChannels.Should().Contain("sms");
    }

    [Fact]
    public async Task Notification_BulkSend_ProcessesMultipleRecipients()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<BulkNotificationState>("bulk-notification")
            .ForEach(
                state => state.Recipients,
                (recipient, f) => f.Step($"send-to-{recipient}", async (state, ct) =>
                {
                    state.SentTo.Add(recipient);
                    return true;
                }))
            .Step("complete", async (state, ct) =>
            {
                state.Completed = true;
                return true;
            })
            .Build();

        var initialState = new BulkNotificationState
        {
            FlowId = "bulk-001",
            Recipients = new List<string> { "user1@test.com", "user2@test.com", "user3@test.com", "user4@test.com", "user5@test.com" },
            Message = "Bulk notification"
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.SentTo.Should().HaveCount(5);
        result.State.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task Notification_ByType_RoutesToCorrectTemplate()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<NotificationState>("type-routing")
            .If(state => state.Type == NotificationType.Critical)
                .Then(f => f.Step("critical-alert", async (state, ct) =>
                {
                    state.Message = $"[CRITICAL] {state.Message}";
                    state.DeliveredChannels.Add("critical-channel");
                    return true;
                }))
            .ElseIf(state => state.Type == NotificationType.Warning)
                .Then(f => f.Step("warning-alert", async (state, ct) =>
                {
                    state.Message = $"[WARNING] {state.Message}";
                    state.DeliveredChannels.Add("warning-channel");
                    return true;
                }))
            .ElseIf(state => state.Type == NotificationType.Marketing)
                .Then(f => f.Step("marketing-send", async (state, ct) =>
                {
                    state.Message = $"[PROMO] {state.Message}";
                    state.DeliveredChannels.Add("marketing-channel");
                    return true;
                }))
            .Else(f => f.Step("info-send", async (state, ct) =>
            {
                state.Message = $"[INFO] {state.Message}";
                state.DeliveredChannels.Add("info-channel");
                return true;
            }))
            .EndIf()
            .Build();

        // Test Critical
        var criticalState = new NotificationState { FlowId = "critical", Type = NotificationType.Critical, Message = "Server down" };
        var criticalResult = await executor.ExecuteAsync(flow, criticalState);
        criticalResult.State.Message.Should().StartWith("[CRITICAL]");
        criticalResult.State.DeliveredChannels.Should().Contain("critical-channel");

        // Test Marketing
        var marketingState = new NotificationState { FlowId = "marketing", Type = NotificationType.Marketing, Message = "Sale today" };
        var marketingResult = await executor.ExecuteAsync(flow, marketingState);
        marketingResult.State.Message.Should().StartWith("[PROMO]");
    }

    public class BulkNotificationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Recipients { get; set; } = new();
        public string Message { get; set; } = "";
        public List<string> SentTo { get; set; } = new();
        public bool Completed { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
