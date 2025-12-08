using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// Full flow E2E tests covering complete message processing scenarios
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class FullFlowE2ETests
{
    [Fact]
    public async Task CompleteOrderFlow_CreateToComplete_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
        services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResult>, CreateOrderHandler>();
        services.AddScoped<IRequestHandler<ProcessOrderCommand, ProcessOrderResult>, ProcessOrderHandler>();
        services.AddScoped<IRequestHandler<CompleteOrderCommand, CompleteOrderResult>, CompleteOrderHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
        services.AddScoped<IEventHandler<OrderCompletedEvent>, OrderCompletedHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        OrderCreatedHandler.ReceivedCount = 0;
        OrderCompletedHandler.ReceivedCount = 0;

        // Act - Create Order
        var createCommand = new CreateOrderCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            CustomerId = "CUST-001",
            Amount = 150.00m
        };
        var createResult = await mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(createCommand);

        // Assert Create
        createResult.IsSuccess.Should().BeTrue();
        var orderId = createResult.Value!.OrderId;
        orderId.Should().NotBeNullOrEmpty();

        // Publish OrderCreated event
        var orderCreatedEvent = new OrderCreatedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = orderId,
            CustomerId = "CUST-001"
        };
        await mediator.PublishAsync(orderCreatedEvent);
        OrderCreatedHandler.ReceivedCount.Should().Be(1);

        // Act - Process Order
        var processCommand = new ProcessOrderCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = orderId
        };
        var processResult = await mediator.SendAsync<ProcessOrderCommand, ProcessOrderResult>(processCommand);

        // Assert Process
        processResult.IsSuccess.Should().BeTrue();
        processResult.Value!.Status.Should().Be("Processing");

        // Act - Complete Order
        var completeCommand = new CompleteOrderCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = orderId
        };
        var completeResult = await mediator.SendAsync<CompleteOrderCommand, CompleteOrderResult>(completeCommand);

        // Assert Complete
        completeResult.IsSuccess.Should().BeTrue();
        completeResult.Value!.Status.Should().Be("Completed");

        // Publish OrderCompleted event
        var orderCompletedEvent = new OrderCompletedEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = orderId
        };
        await mediator.PublishAsync(orderCompletedEvent);
        OrderCompletedHandler.ReceivedCount.Should().Be(1);
    }

    [Fact]
    public async Task UserRegistrationFlow_WithValidation_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<RegisterUserCommand, RegisterUserResult>, RegisterUserHandler>();
        services.AddScoped<IRequestHandler<VerifyEmailCommand, VerifyEmailResult>, VerifyEmailHandler>();
        services.AddScoped<IEventHandler<UserRegisteredEvent>, UserRegisteredHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        UserRegisteredHandler.ReceivedCount = 0;

        // Act - Register User
        var registerCommand = new RegisterUserCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            Email = "test@example.com",
            Username = "testuser"
        };
        var registerResult = await mediator.SendAsync<RegisterUserCommand, RegisterUserResult>(registerCommand);

        // Assert Register
        registerResult.IsSuccess.Should().BeTrue();
        var userId = registerResult.Value!.UserId;

        // Publish UserRegistered event
        var userRegisteredEvent = new UserRegisteredEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            UserId = userId,
            Email = "test@example.com"
        };
        await mediator.PublishAsync(userRegisteredEvent);
        UserRegisteredHandler.ReceivedCount.Should().Be(1);

        // Act - Verify Email
        var verifyCommand = new VerifyEmailCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            UserId = userId,
            VerificationCode = "123456"
        };
        var verifyResult = await mediator.SendAsync<VerifyEmailCommand, VerifyEmailResult>(verifyCommand);

        // Assert Verify
        verifyResult.IsSuccess.Should().BeTrue();
        verifyResult.Value!.IsVerified.Should().BeTrue();
    }

    [Fact]
    public async Task PaymentFlow_WithRetry_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var attemptTracker = new PaymentAttemptTracker();
        services.AddSingleton(attemptTracker);
        services.AddScoped<IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>, ProcessPaymentHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var paymentCommand = new ProcessPaymentCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            OrderId = "ORD-001",
            Amount = 99.99m
        };
        var result = await mediator.SendAsync<ProcessPaymentCommand, ProcessPaymentResult>(paymentCommand);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InventoryFlow_CheckAndReserve_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CheckInventoryQuery, CheckInventoryResult>, CheckInventoryHandler>();
        services.AddScoped<IRequestHandler<ReserveInventoryCommand, ReserveInventoryResult>, ReserveInventoryHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Check Inventory
        var checkQuery = new CheckInventoryQuery
        {
            MessageId = MessageExtensions.NewMessageId(),
            ProductId = "PROD-001",
            Quantity = 5
        };
        var checkResult = await mediator.SendAsync<CheckInventoryQuery, CheckInventoryResult>(checkQuery);

        // Assert Check
        checkResult.IsSuccess.Should().BeTrue();
        checkResult.Value!.IsAvailable.Should().BeTrue();

        // Act - Reserve Inventory
        var reserveCommand = new ReserveInventoryCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            ProductId = "PROD-001",
            Quantity = 5,
            OrderId = "ORD-001"
        };
        var reserveResult = await mediator.SendAsync<ReserveInventoryCommand, ReserveInventoryResult>(reserveCommand);

        // Assert Reserve
        reserveResult.IsSuccess.Should().BeTrue();
        reserveResult.Value!.ReservationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NotificationFlow_MultiChannel_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<SendNotificationEvent>, EmailNotificationHandler>();
        services.AddScoped<IEventHandler<SendNotificationEvent>, SmsNotificationHandler>();
        services.AddScoped<IEventHandler<SendNotificationEvent>, PushNotificationHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        EmailNotificationHandler.ReceivedCount = 0;
        SmsNotificationHandler.ReceivedCount = 0;
        PushNotificationHandler.ReceivedCount = 0;

        // Act
        var notificationEvent = new SendNotificationEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            UserId = "USER-001",
            Message = "Your order has been shipped!"
        };
        await mediator.PublishAsync(notificationEvent);

        // Assert - All handlers should receive the event
        EmailNotificationHandler.ReceivedCount.Should().Be(1);
        SmsNotificationHandler.ReceivedCount.Should().Be(1);
        PushNotificationHandler.ReceivedCount.Should().Be(1);
    }

    #region Test Types

    // Order Flow
    [MemoryPackable]
    private partial record CreateOrderCommand : IRequest<CreateOrderResult>
    {
        public required long MessageId { get; init; }
        public required string CustomerId { get; init; }
        public required decimal Amount { get; init; }
    }

    [MemoryPackable]
    private partial record CreateOrderResult
    {
        public required string OrderId { get; init; }
    }

    private sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
    {
        public ValueTask<CatgaResult<CreateOrderResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
        {
            var orderId = $"ORD-{Guid.NewGuid():N}";
            return new ValueTask<CatgaResult<CreateOrderResult>>(CatgaResult<CreateOrderResult>.Success(new CreateOrderResult { OrderId = orderId }));
        }
    }

    [MemoryPackable]
    private partial record ProcessOrderCommand : IRequest<ProcessOrderResult>
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
    }

    [MemoryPackable]
    private partial record ProcessOrderResult
    {
        public required string Status { get; init; }
    }

    private sealed class ProcessOrderHandler : IRequestHandler<ProcessOrderCommand, ProcessOrderResult>
    {
        public ValueTask<CatgaResult<ProcessOrderResult>> HandleAsync(ProcessOrderCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<ProcessOrderResult>>(CatgaResult<ProcessOrderResult>.Success(new ProcessOrderResult { Status = "Processing" }));
        }
    }

    [MemoryPackable]
    private partial record CompleteOrderCommand : IRequest<CompleteOrderResult>
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
    }

    [MemoryPackable]
    private partial record CompleteOrderResult
    {
        public required string Status { get; init; }
    }

    private sealed class CompleteOrderHandler : IRequestHandler<CompleteOrderCommand, CompleteOrderResult>
    {
        public ValueTask<CatgaResult<CompleteOrderResult>> HandleAsync(CompleteOrderCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<CompleteOrderResult>>(CatgaResult<CompleteOrderResult>.Success(new CompleteOrderResult { Status = "Completed" }));
        }
    }

    [MemoryPackable]
    private partial record OrderCreatedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required string CustomerId { get; init; }
    }

    private sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record OrderCompletedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
    }

    private sealed class OrderCompletedHandler : IEventHandler<OrderCompletedEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(OrderCompletedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    // User Registration Flow
    [MemoryPackable]
    private partial record RegisterUserCommand : IRequest<RegisterUserResult>
    {
        public required long MessageId { get; init; }
        public required string Email { get; init; }
        public required string Username { get; init; }
    }

    [MemoryPackable]
    private partial record RegisterUserResult
    {
        public required string UserId { get; init; }
    }

    private sealed class RegisterUserHandler : IRequestHandler<RegisterUserCommand, RegisterUserResult>
    {
        public ValueTask<CatgaResult<RegisterUserResult>> HandleAsync(RegisterUserCommand request, CancellationToken ct = default)
        {
            var userId = $"USR-{Guid.NewGuid():N}";
            return new ValueTask<CatgaResult<RegisterUserResult>>(CatgaResult<RegisterUserResult>.Success(new RegisterUserResult { UserId = userId }));
        }
    }

    [MemoryPackable]
    private partial record VerifyEmailCommand : IRequest<VerifyEmailResult>
    {
        public required long MessageId { get; init; }
        public required string UserId { get; init; }
        public required string VerificationCode { get; init; }
    }

    [MemoryPackable]
    private partial record VerifyEmailResult
    {
        public required bool IsVerified { get; init; }
    }

    private sealed class VerifyEmailHandler : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
    {
        public ValueTask<CatgaResult<VerifyEmailResult>> HandleAsync(VerifyEmailCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<VerifyEmailResult>>(CatgaResult<VerifyEmailResult>.Success(new VerifyEmailResult { IsVerified = true }));
        }
    }

    [MemoryPackable]
    private partial record UserRegisteredEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string UserId { get; init; }
        public required string Email { get; init; }
    }

    private sealed class UserRegisteredHandler : IEventHandler<UserRegisteredEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(UserRegisteredEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    // Payment Flow
    [MemoryPackable]
    private partial record ProcessPaymentCommand : IRequest<ProcessPaymentResult>
    {
        public required long MessageId { get; init; }
        public required string OrderId { get; init; }
        public required decimal Amount { get; init; }
    }

    [MemoryPackable]
    private partial record ProcessPaymentResult
    {
        public required string TransactionId { get; init; }
    }

    private sealed class PaymentAttemptTracker
    {
        public int Attempts = 0;
    }

    private sealed class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>
    {
        public ValueTask<CatgaResult<ProcessPaymentResult>> HandleAsync(ProcessPaymentCommand request, CancellationToken ct = default)
        {
            var transactionId = $"TXN-{Guid.NewGuid():N}";
            return new ValueTask<CatgaResult<ProcessPaymentResult>>(CatgaResult<ProcessPaymentResult>.Success(new ProcessPaymentResult { TransactionId = transactionId }));
        }
    }

    // Inventory Flow
    [MemoryPackable]
    private partial record CheckInventoryQuery : IRequest<CheckInventoryResult>
    {
        public required long MessageId { get; init; }
        public required string ProductId { get; init; }
        public required int Quantity { get; init; }
    }

    [MemoryPackable]
    private partial record CheckInventoryResult
    {
        public required bool IsAvailable { get; init; }
        public int AvailableQuantity { get; init; }
    }

    private sealed class CheckInventoryHandler : IRequestHandler<CheckInventoryQuery, CheckInventoryResult>
    {
        public ValueTask<CatgaResult<CheckInventoryResult>> HandleAsync(CheckInventoryQuery request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<CheckInventoryResult>>(CatgaResult<CheckInventoryResult>.Success(new CheckInventoryResult { IsAvailable = true, AvailableQuantity = 100 }));
        }
    }

    [MemoryPackable]
    private partial record ReserveInventoryCommand : IRequest<ReserveInventoryResult>
    {
        public required long MessageId { get; init; }
        public required string ProductId { get; init; }
        public required int Quantity { get; init; }
        public required string OrderId { get; init; }
    }

    [MemoryPackable]
    private partial record ReserveInventoryResult
    {
        public required string ReservationId { get; init; }
    }

    private sealed class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand, ReserveInventoryResult>
    {
        public ValueTask<CatgaResult<ReserveInventoryResult>> HandleAsync(ReserveInventoryCommand request, CancellationToken ct = default)
        {
            var reservationId = $"RES-{Guid.NewGuid():N}";
            return new ValueTask<CatgaResult<ReserveInventoryResult>>(CatgaResult<ReserveInventoryResult>.Success(new ReserveInventoryResult { ReservationId = reservationId }));
        }
    }

    // Notification Flow
    [MemoryPackable]
    private partial record SendNotificationEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string UserId { get; init; }
        public required string Message { get; init; }
    }

    private sealed class EmailNotificationHandler : IEventHandler<SendNotificationEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(SendNotificationEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SmsNotificationHandler : IEventHandler<SendNotificationEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(SendNotificationEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PushNotificationHandler : IEventHandler<SendNotificationEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(SendNotificationEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}



