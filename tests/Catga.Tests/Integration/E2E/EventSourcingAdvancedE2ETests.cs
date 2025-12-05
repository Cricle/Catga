using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// Advanced E2E tests for event sourcing patterns
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class EventSourcingAdvancedE2ETests
{
    [Fact]
    public async Task Aggregate_MultipleEvents_StateRebuilt()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var eventStore = new InMemoryEventStore();
        services.AddSingleton(eventStore);
        services.AddScoped<IRequestHandler<CreateAccountCommand, AccountResult>, CreateAccountHandler>();
        services.AddScoped<IRequestHandler<DepositCommand, AccountResult>, DepositHandler>();
        services.AddScoped<IRequestHandler<WithdrawCommand, AccountResult>, WithdrawHandler>();
        services.AddScoped<IRequestHandler<GetAccountQuery, AccountState>, GetAccountHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var accountId = Guid.NewGuid().ToString();

        // Act - Create account
        var createResult = await mediator.SendAsync<CreateAccountCommand, AccountResult>(
            new CreateAccountCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, InitialBalance = 100m });
        createResult.IsSuccess.Should().BeTrue();

        // Deposit
        var depositResult = await mediator.SendAsync<DepositCommand, AccountResult>(
            new DepositCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, Amount = 50m });
        depositResult.IsSuccess.Should().BeTrue();

        // Withdraw
        var withdrawResult = await mediator.SendAsync<WithdrawCommand, AccountResult>(
            new WithdrawCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, Amount = 30m });
        withdrawResult.IsSuccess.Should().BeTrue();

        // Query state
        var stateResult = await mediator.SendAsync<GetAccountQuery, AccountState>(
            new GetAccountQuery { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId });

        // Assert
        stateResult.IsSuccess.Should().BeTrue();
        stateResult.Value!.Balance.Should().Be(120m); // 100 + 50 - 30
        stateResult.Value.Version.Should().Be(3);
    }

    [Fact]
    public async Task Aggregate_ConcurrentModifications_OptimisticLocking()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var eventStore = new InMemoryEventStore();
        services.AddSingleton(eventStore);
        services.AddScoped<IRequestHandler<CreateAccountCommand, AccountResult>, CreateAccountHandler>();
        services.AddScoped<IRequestHandler<DepositWithVersionCommand, AccountResult>, DepositWithVersionHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var accountId = Guid.NewGuid().ToString();

        // Create account
        await mediator.SendAsync<CreateAccountCommand, AccountResult>(
            new CreateAccountCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, InitialBalance = 100m });

        // Act - Concurrent deposits with same expected version
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            return await mediator.SendAsync<DepositWithVersionCommand, AccountResult>(
                new DepositWithVersionCommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    AccountId = accountId,
                    Amount = 10m,
                    ExpectedVersion = 1 // All expect version 1
                });
        });

        var results = await Task.WhenAll(tasks);

        // Assert - Only one should succeed due to optimistic locking
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().Be(1);
        failureCount.Should().Be(4);
    }

    [Fact]
    public async Task EventReplay_FromSnapshot_CorrectState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var eventStore = new InMemoryEventStore();
        var snapshotStore = new InMemorySnapshotStore();
        services.AddSingleton(eventStore);
        services.AddSingleton(snapshotStore);
        services.AddScoped<IRequestHandler<CreateAccountCommand, AccountResult>, CreateAccountHandler>();
        services.AddScoped<IRequestHandler<DepositCommand, AccountResult>, DepositHandler>();
        services.AddScoped<IRequestHandler<CreateSnapshotCommand, SnapshotResult>, CreateSnapshotHandler>();
        services.AddScoped<IRequestHandler<GetAccountFromSnapshotQuery, AccountState>, GetAccountFromSnapshotHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var accountId = Guid.NewGuid().ToString();

        // Create and modify account
        await mediator.SendAsync<CreateAccountCommand, AccountResult>(
            new CreateAccountCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, InitialBalance = 100m });

        for (int i = 0; i < 10; i++)
        {
            await mediator.SendAsync<DepositCommand, AccountResult>(
                new DepositCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, Amount = 10m });
        }

        // Create snapshot at version 5
        await mediator.SendAsync<CreateSnapshotCommand, SnapshotResult>(
            new CreateSnapshotCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, AtVersion = 5 });

        // Add more events after snapshot
        for (int i = 0; i < 5; i++)
        {
            await mediator.SendAsync<DepositCommand, AccountResult>(
                new DepositCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, Amount = 5m });
        }

        // Act - Get state from snapshot + events
        var stateResult = await mediator.SendAsync<GetAccountFromSnapshotQuery, AccountState>(
            new GetAccountFromSnapshotQuery { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId });

        // Assert
        stateResult.IsSuccess.Should().BeTrue();
        // 100 + 10*10 + 5*5 = 100 + 100 + 25 = 225
        stateResult.Value!.Balance.Should().Be(225m);
    }

    [Fact]
    public async Task EventProjection_MultipleProjectors_AllUpdated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var eventStore = new InMemoryEventStore();
        services.AddSingleton(eventStore);
        services.AddScoped<IRequestHandler<CreateAccountCommand, AccountResult>, CreateAccountHandler>();
        services.AddScoped<IEventHandler<AccountCreatedEvent>, AccountSummaryProjector>();
        services.AddScoped<IEventHandler<AccountCreatedEvent>, AccountAuditProjector>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        AccountSummaryProjector.ProjectedCount = 0;
        AccountAuditProjector.ProjectedCount = 0;

        // Act
        for (int i = 0; i < 5; i++)
        {
            var accountId = Guid.NewGuid().ToString();
            await mediator.SendAsync<CreateAccountCommand, AccountResult>(
                new CreateAccountCommand { MessageId = MessageExtensions.NewMessageId(), AccountId = accountId, InitialBalance = 100m });

            // Simulate publishing event to projectors
            await mediator.PublishAsync(new AccountCreatedEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                AccountId = accountId,
                InitialBalance = 100m
            });
        }

        // Assert
        AccountSummaryProjector.ProjectedCount.Should().Be(5);
        AccountAuditProjector.ProjectedCount.Should().Be(5);
    }

    #region Event Store

    private class InMemoryEventStore
    {
        private readonly Dictionary<string, List<AccountEvent>> _events = new();
        private readonly object _lock = new();

        public void Append(string aggregateId, AccountEvent @event, int expectedVersion)
        {
            lock (_lock)
            {
                if (!_events.ContainsKey(aggregateId))
                    _events[aggregateId] = new List<AccountEvent>();

                var currentVersion = _events[aggregateId].Count;
                if (currentVersion != expectedVersion)
                    throw new InvalidOperationException($"Concurrency conflict: expected {expectedVersion}, got {currentVersion}");

                _events[aggregateId].Add(@event);
            }
        }

        public List<AccountEvent> GetEvents(string aggregateId)
        {
            lock (_lock)
            {
                return _events.TryGetValue(aggregateId, out var events)
                    ? new List<AccountEvent>(events)
                    : new List<AccountEvent>();
            }
        }

        public List<AccountEvent> GetEventsFromVersion(string aggregateId, int fromVersion)
        {
            lock (_lock)
            {
                if (!_events.TryGetValue(aggregateId, out var events))
                    return new List<AccountEvent>();

                return events.Skip(fromVersion).ToList();
            }
        }
    }

    private class InMemorySnapshotStore
    {
        private readonly Dictionary<string, AccountSnapshot> _snapshots = new();

        public void Save(string aggregateId, AccountSnapshot snapshot)
        {
            _snapshots[aggregateId] = snapshot;
        }

        public AccountSnapshot? Get(string aggregateId)
        {
            return _snapshots.TryGetValue(aggregateId, out var snapshot) ? snapshot : null;
        }
    }

    private record AccountSnapshot(decimal Balance, int Version);

    private abstract record AccountEvent
    {
        public required string AccountId { get; init; }
        public required decimal Amount { get; init; }
    }

    private record AccountCreatedEventInternal : AccountEvent;
    private record DepositedEvent : AccountEvent;
    private record WithdrawnEvent : AccountEvent;

    #endregion

    #region Commands and Handlers

    [MemoryPackable]
    private partial record AccountResult
    {
        public required string AccountId { get; init; }
        public required decimal Balance { get; init; }
        public required int Version { get; init; }
    }

    [MemoryPackable]
    private partial record AccountState
    {
        public required string AccountId { get; init; }
        public required decimal Balance { get; init; }
        public required int Version { get; init; }
    }

    [MemoryPackable]
    private partial record CreateAccountCommand : IRequest<AccountResult>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
        public required decimal InitialBalance { get; init; }
    }

    private sealed class CreateAccountHandler(InMemoryEventStore eventStore) : IRequestHandler<CreateAccountCommand, AccountResult>
    {
        public Task<CatgaResult<AccountResult>> HandleAsync(CreateAccountCommand request, CancellationToken ct = default)
        {
            var @event = new AccountCreatedEventInternal { AccountId = request.AccountId, Amount = request.InitialBalance };
            eventStore.Append(request.AccountId, @event, 0);
            return Task.FromResult(CatgaResult<AccountResult>.Success(
                new AccountResult { AccountId = request.AccountId, Balance = request.InitialBalance, Version = 1 }));
        }
    }

    [MemoryPackable]
    private partial record DepositCommand : IRequest<AccountResult>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
        public required decimal Amount { get; init; }
    }

    private sealed class DepositHandler(InMemoryEventStore eventStore) : IRequestHandler<DepositCommand, AccountResult>
    {
        public Task<CatgaResult<AccountResult>> HandleAsync(DepositCommand request, CancellationToken ct = default)
        {
            var events = eventStore.GetEvents(request.AccountId);
            var currentVersion = events.Count;
            var currentBalance = events.Sum(e => e.Amount);

            var @event = new DepositedEvent { AccountId = request.AccountId, Amount = request.Amount };
            eventStore.Append(request.AccountId, @event, currentVersion);

            return Task.FromResult(CatgaResult<AccountResult>.Success(
                new AccountResult { AccountId = request.AccountId, Balance = currentBalance + request.Amount, Version = currentVersion + 1 }));
        }
    }

    [MemoryPackable]
    private partial record WithdrawCommand : IRequest<AccountResult>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
        public required decimal Amount { get; init; }
    }

    private sealed class WithdrawHandler(InMemoryEventStore eventStore) : IRequestHandler<WithdrawCommand, AccountResult>
    {
        public Task<CatgaResult<AccountResult>> HandleAsync(WithdrawCommand request, CancellationToken ct = default)
        {
            var events = eventStore.GetEvents(request.AccountId);
            var currentVersion = events.Count;
            var currentBalance = events.Sum(e => e is WithdrawnEvent ? -e.Amount : e.Amount);

            if (currentBalance < request.Amount)
                return Task.FromResult(CatgaResult<AccountResult>.Failure("Insufficient funds"));

            var @event = new WithdrawnEvent { AccountId = request.AccountId, Amount = request.Amount };
            eventStore.Append(request.AccountId, @event, currentVersion);

            return Task.FromResult(CatgaResult<AccountResult>.Success(
                new AccountResult { AccountId = request.AccountId, Balance = currentBalance - request.Amount, Version = currentVersion + 1 }));
        }
    }

    [MemoryPackable]
    private partial record GetAccountQuery : IRequest<AccountState>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
    }

    private sealed class GetAccountHandler(InMemoryEventStore eventStore) : IRequestHandler<GetAccountQuery, AccountState>
    {
        public Task<CatgaResult<AccountState>> HandleAsync(GetAccountQuery request, CancellationToken ct = default)
        {
            var events = eventStore.GetEvents(request.AccountId);
            var balance = events.Sum(e => e is WithdrawnEvent ? -e.Amount : e.Amount);
            return Task.FromResult(CatgaResult<AccountState>.Success(
                new AccountState { AccountId = request.AccountId, Balance = balance, Version = events.Count }));
        }
    }

    [MemoryPackable]
    private partial record DepositWithVersionCommand : IRequest<AccountResult>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
        public required decimal Amount { get; init; }
        public required int ExpectedVersion { get; init; }
    }

    private sealed class DepositWithVersionHandler(InMemoryEventStore eventStore) : IRequestHandler<DepositWithVersionCommand, AccountResult>
    {
        public Task<CatgaResult<AccountResult>> HandleAsync(DepositWithVersionCommand request, CancellationToken ct = default)
        {
            try
            {
                var events = eventStore.GetEvents(request.AccountId);
                var currentBalance = events.Sum(e => e is WithdrawnEvent ? -e.Amount : e.Amount);

                var @event = new DepositedEvent { AccountId = request.AccountId, Amount = request.Amount };
                eventStore.Append(request.AccountId, @event, request.ExpectedVersion);

                return Task.FromResult(CatgaResult<AccountResult>.Success(
                    new AccountResult { AccountId = request.AccountId, Balance = currentBalance + request.Amount, Version = request.ExpectedVersion + 1 }));
            }
            catch (InvalidOperationException ex)
            {
                return Task.FromResult(CatgaResult<AccountResult>.Failure(ex.Message));
            }
        }
    }

    [MemoryPackable]
    private partial record SnapshotResult
    {
        public required string AccountId { get; init; }
        public required int Version { get; init; }
    }

    [MemoryPackable]
    private partial record CreateSnapshotCommand : IRequest<SnapshotResult>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
        public required int AtVersion { get; init; }
    }

    private sealed class CreateSnapshotHandler(InMemoryEventStore eventStore, InMemorySnapshotStore snapshotStore)
        : IRequestHandler<CreateSnapshotCommand, SnapshotResult>
    {
        public Task<CatgaResult<SnapshotResult>> HandleAsync(CreateSnapshotCommand request, CancellationToken ct = default)
        {
            var events = eventStore.GetEvents(request.AccountId).Take(request.AtVersion).ToList();
            var balance = events.Sum(e => e is WithdrawnEvent ? -e.Amount : e.Amount);

            snapshotStore.Save(request.AccountId, new AccountSnapshot(balance, request.AtVersion));

            return Task.FromResult(CatgaResult<SnapshotResult>.Success(
                new SnapshotResult { AccountId = request.AccountId, Version = request.AtVersion }));
        }
    }

    [MemoryPackable]
    private partial record GetAccountFromSnapshotQuery : IRequest<AccountState>
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
    }

    private sealed class GetAccountFromSnapshotHandler(InMemoryEventStore eventStore, InMemorySnapshotStore snapshotStore)
        : IRequestHandler<GetAccountFromSnapshotQuery, AccountState>
    {
        public Task<CatgaResult<AccountState>> HandleAsync(GetAccountFromSnapshotQuery request, CancellationToken ct = default)
        {
            var snapshot = snapshotStore.Get(request.AccountId);
            var startVersion = snapshot?.Version ?? 0;
            var startBalance = snapshot?.Balance ?? 0m;

            var events = eventStore.GetEventsFromVersion(request.AccountId, startVersion);
            var balance = startBalance + events.Sum(e => e is WithdrawnEvent ? -e.Amount : e.Amount);

            return Task.FromResult(CatgaResult<AccountState>.Success(
                new AccountState { AccountId = request.AccountId, Balance = balance, Version = startVersion + events.Count }));
        }
    }

    [MemoryPackable]
    private partial record AccountCreatedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string AccountId { get; init; }
        public required decimal InitialBalance { get; init; }
    }

    private sealed class AccountSummaryProjector : IEventHandler<AccountCreatedEvent>
    {
        public static int ProjectedCount;
        public Task HandleAsync(AccountCreatedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ProjectedCount);
            return Task.CompletedTask;
        }
    }

    private sealed class AccountAuditProjector : IEventHandler<AccountCreatedEvent>
    {
        public static int ProjectedCount;
        public Task HandleAsync(AccountCreatedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ProjectedCount);
            return Task.CompletedTask;
        }
    }

    #endregion
}
