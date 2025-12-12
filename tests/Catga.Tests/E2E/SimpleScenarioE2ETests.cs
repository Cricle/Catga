using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.E2E;

/// <summary>
/// Simple scenario E2E tests covering basic workflows.
/// Tests straightforward business logic with minimal branching.
/// </summary>
public class SimpleScenarioE2ETests
{
    private readonly ITestOutputHelper _output;

    public SimpleScenarioE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task E2E_SimpleRegistration_BasicFlow()
    {
        // Arrange - Basic user registration
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleRegistrationState, SimpleRegistrationFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleRegistrationState, SimpleRegistrationFlow>>();

        var registration = new SimpleRegistrationState
        {
            FlowId = "reg-001",
            UserId = "USER-001",
            Email = "user@example.com",
            Password = "SecurePass123"
        };

        // Act
        var result = await executor!.RunAsync(registration);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.EmailValidated.Should().BeTrue();
        result.State.PasswordValidated.Should().BeTrue();
        result.State.AccountCreated.Should().BeTrue();
        result.State.ConfirmationSent.Should().BeTrue();

        _output.WriteLine($"✓ Simple registration completed");
    }

    [Fact]
    public async Task E2E_SimpleLogin_SuccessfulAuth()
    {
        // Arrange - Simple login
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleLoginState, SimpleLoginFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleLoginState, SimpleLoginFlow>>();

        var login = new SimpleLoginState
        {
            FlowId = "login-001",
            Username = "user@example.com",
            Password = "SecurePass123",
            RememberMe = false
        };

        // Act
        var result = await executor!.RunAsync(login);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CredentialsValid.Should().BeTrue();
        result.State.SessionCreated.Should().BeTrue();
        result.State.LoggedIn.Should().BeTrue();

        _output.WriteLine($"✓ Simple login successful");
    }

    [Fact]
    public async Task E2E_SimplePasswordReset_BasicFlow()
    {
        // Arrange - Simple password reset
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimplePasswordResetState, SimplePasswordResetFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimplePasswordResetState, SimplePasswordResetFlow>>();

        var reset = new SimplePasswordResetState
        {
            FlowId = "reset-001",
            Email = "user@example.com",
            NewPassword = "NewSecurePass456"
        };

        // Act
        var result = await executor!.RunAsync(reset);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.EmailVerified.Should().BeTrue();
        result.State.TokenGenerated.Should().BeTrue();
        result.State.PasswordUpdated.Should().BeTrue();
        result.State.ConfirmationEmailSent.Should().BeTrue();

        _output.WriteLine($"✓ Simple password reset completed");
    }

    [Fact]
    public async Task E2E_SimpleProfileUpdate_BasicFlow()
    {
        // Arrange - Simple profile update
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleProfileUpdateState, SimpleProfileUpdateFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleProfileUpdateState, SimpleProfileUpdateFlow>>();

        var update = new SimpleProfileUpdateState
        {
            FlowId = "profile-001",
            UserId = "USER-001",
            FullName = "John Doe",
            PhoneNumber = "+1234567890",
            Address = "123 Main St"
        };

        // Act
        var result = await executor!.RunAsync(update);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.DataValidated.Should().BeTrue();
        result.State.ProfileUpdated.Should().BeTrue();
        result.State.AuditLogged.Should().BeTrue();

        _output.WriteLine($"✓ Simple profile update completed");
    }

    [Fact]
    public async Task E2E_SimpleNotification_BasicFlow()
    {
        // Arrange - Simple notification sending
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleNotificationState, SimpleNotificationFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleNotificationState, SimpleNotificationFlow>>();

        var notification = new SimpleNotificationState
        {
            FlowId = "notif-001",
            RecipientId = "USER-001",
            RecipientEmail = "user@example.com",
            Subject = "Welcome",
            Message = "Welcome to our service"
        };

        // Act
        var result = await executor!.RunAsync(notification);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.MessageFormatted.Should().BeTrue();
        result.State.EmailSent.Should().BeTrue();
        result.State.LogRecorded.Should().BeTrue();

        _output.WriteLine($"✓ Simple notification sent");
    }

    [Fact]
    public async Task E2E_SimpleDataExport_BasicFlow()
    {
        // Arrange - Simple data export
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SimpleDataExportState, SimpleDataExportFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleDataExportState, SimpleDataExportFlow>>();

        var export = new SimpleDataExportState
        {
            FlowId = "export-001",
            UserId = "USER-001",
            Format = "CSV",
            DataType = "Orders"
        };

        // Act
        var result = await executor!.RunAsync(export);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.DataCollected.Should().BeTrue();
        result.State.FileGenerated.Should().BeTrue();
        result.State.DownloadLinkCreated.Should().BeTrue();
        result.State.NotificationSent.Should().BeTrue();

        _output.WriteLine($"✓ Simple data export completed");
    }
}

// ========== Flow Configurations ==========

public class SimpleRegistrationFlow : FlowConfig<SimpleRegistrationState>
{
    protected override void Configure(IFlowBuilder<SimpleRegistrationState> flow)
    {
        flow.Name("simple-registration");
        flow.Step("validate-email", s => s.EmailValidated = true);
        flow.Step("validate-password", s => s.PasswordValidated = true);
        flow.Step("create-account", s => s.AccountCreated = true);
        flow.Step("send-confirmation", s => s.ConfirmationSent = true);
    }
}

public class SimpleLoginFlow : FlowConfig<SimpleLoginState>
{
    protected override void Configure(IFlowBuilder<SimpleLoginState> flow)
    {
        flow.Name("simple-login");
        flow.Step("validate-credentials", s => s.CredentialsValid = true);
        flow.Step("create-session", s => s.SessionCreated = true);
        flow.Step("set-logged-in", s => s.LoggedIn = true);
    }
}

public class SimplePasswordResetFlow : FlowConfig<SimplePasswordResetState>
{
    protected override void Configure(IFlowBuilder<SimplePasswordResetState> flow)
    {
        flow.Name("simple-password-reset");
        flow.Step("verify-email", s => s.EmailVerified = true);
        flow.Step("generate-token", s => s.TokenGenerated = true);
        flow.Step("update-password", s => s.PasswordUpdated = true);
        flow.Step("send-confirmation", s => s.ConfirmationEmailSent = true);
    }
}

public class SimpleProfileUpdateFlow : FlowConfig<SimpleProfileUpdateState>
{
    protected override void Configure(IFlowBuilder<SimpleProfileUpdateState> flow)
    {
        flow.Name("simple-profile-update");
        flow.Step("validate-data", s => s.DataValidated = true);
        flow.Step("update-profile", s => s.ProfileUpdated = true);
        flow.Step("log-audit", s => s.AuditLogged = true);
    }
}

public class SimpleNotificationFlow : FlowConfig<SimpleNotificationState>
{
    protected override void Configure(IFlowBuilder<SimpleNotificationState> flow)
    {
        flow.Name("simple-notification");
        flow.Step("format-message", s => s.MessageFormatted = true);
        flow.Step("send-email", s => s.EmailSent = true);
        flow.Step("record-log", s => s.LogRecorded = true);
    }
}

public class SimpleDataExportFlow : FlowConfig<SimpleDataExportState>
{
    protected override void Configure(IFlowBuilder<SimpleDataExportState> flow)
    {
        flow.Name("simple-data-export");
        flow.Step("collect-data", s => s.DataCollected = true);
        flow.Step("generate-file", s => s.FileGenerated = true);
        flow.Step("create-download-link", s => s.DownloadLinkCreated = true);
        flow.Step("send-notification", s => s.NotificationSent = true);
    }
}

// ========== States ==========

public class SimpleRegistrationState : IFlowState
{
    public string? FlowId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EmailValidated { get; set; }
    public bool PasswordValidated { get; set; }
    public bool AccountCreated { get; set; }
    public bool ConfirmationSent { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SimpleLoginState : IFlowState
{
    public string? FlowId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public bool CredentialsValid { get; set; }
    public bool SessionCreated { get; set; }
    public bool LoggedIn { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SimplePasswordResetState : IFlowState
{
    public string? FlowId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public bool TokenGenerated { get; set; }
    public bool PasswordUpdated { get; set; }
    public bool ConfirmationEmailSent { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SimpleProfileUpdateState : IFlowState
{
    public string? FlowId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool DataValidated { get; set; }
    public bool ProfileUpdated { get; set; }
    public bool AuditLogged { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SimpleNotificationState : IFlowState
{
    public string? FlowId { get; set; }
    public string RecipientId { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool MessageFormatted { get; set; }
    public bool EmailSent { get; set; }
    public bool LogRecorded { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SimpleDataExportState : IFlowState
{
    public string? FlowId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool DataCollected { get; set; }
    public bool FileGenerated { get; set; }
    public bool DownloadLinkCreated { get; set; }
    public bool NotificationSent { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
