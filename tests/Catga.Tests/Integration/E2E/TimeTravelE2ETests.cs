using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E integration tests for time travel functionality.
/// Tests the complete flow from event store through time travel service.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TimeTravelE2ETests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly IServiceProvider _serviceProvider;

    public TimeTravelE2ETests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton<IEventStore>(_eventStore);
        services.AddTimeTravelService<BankAccount>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task TimeTravel_FullWorkflow_ReconstructsHistoricalStates()
    {
        // Arrange - Create a bank account with multiple transactions
        var accountId = "account-001";
        var streamId = $"BankAccount-{accountId}";

        var events = new IEvent[]
        {
            new AccountOpenedEvent { AccountId = accountId, OwnerName = "John Doe", InitialBalance = 1000m },
            new DepositMadeEvent { AccountId = accountId, Amount = 500m },
            new WithdrawalMadeEvent { AccountId = accountId, Amount = 200m },
            new DepositMadeEvent { AccountId = accountId, Amount = 300m },
            new AccountClosedEvent { AccountId = accountId }
        };

        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act & Assert - Check state at each version
        var stateAtV0 = await timeTravelService.GetStateAtVersionAsync(accountId, 0);
        stateAtV0.Should().NotBeNull();
        stateAtV0!.Balance.Should().Be(1000m);
        stateAtV0.IsClosed.Should().BeFalse();

        var stateAtV1 = await timeTravelService.GetStateAtVersionAsync(accountId, 1);
        stateAtV1!.Balance.Should().Be(1500m); // 1000 + 500

        var stateAtV2 = await timeTravelService.GetStateAtVersionAsync(accountId, 2);
        stateAtV2!.Balance.Should().Be(1300m); // 1500 - 200

        var stateAtV3 = await timeTravelService.GetStateAtVersionAsync(accountId, 3);
        stateAtV3!.Balance.Should().Be(1600m); // 1300 + 300

        var stateAtV4 = await timeTravelService.GetStateAtVersionAsync(accountId, 4);
        stateAtV4!.Balance.Should().Be(1600m);
        stateAtV4.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task TimeTravel_CompareVersions_ShowsCompleteHistory()
    {
        // Arrange
        var accountId = "account-002";
        var streamId = $"BankAccount-{accountId}";

        var events = new IEvent[]
        {
            new AccountOpenedEvent { AccountId = accountId, OwnerName = "Jane Doe", InitialBalance = 500m },
            new DepositMadeEvent { AccountId = accountId, Amount = 1000m },
            new WithdrawalMadeEvent { AccountId = accountId, Amount = 750m }
        };

        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act
        var comparison = await timeTravelService.CompareVersionsAsync(accountId, 0, 2);

        // Assert
        comparison.FromState!.Balance.Should().Be(500m);
        comparison.ToState!.Balance.Should().Be(750m); // 500 + 1000 - 750
        comparison.EventsBetween.Should().HaveCount(2);
        comparison.EventsBetween[0].EventType.Should().Be("DepositMadeEvent");
        comparison.EventsBetween[1].EventType.Should().Be("WithdrawalMadeEvent");
    }

    [Fact]
    public async Task TimeTravel_VersionHistory_ProvidesAuditTrail()
    {
        // Arrange
        var accountId = "account-003";
        var streamId = $"BankAccount-{accountId}";

        var events = new IEvent[]
        {
            new AccountOpenedEvent { AccountId = accountId, OwnerName = "Bob Smith", InitialBalance = 100m },
            new DepositMadeEvent { AccountId = accountId, Amount = 50m },
            new DepositMadeEvent { AccountId = accountId, Amount = 25m }
        };

        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act
        var history = await timeTravelService.GetVersionHistoryAsync(accountId);

        // Assert
        history.Should().HaveCount(3);
        history[0].Version.Should().Be(0);
        history[0].EventType.Should().Be("AccountOpenedEvent");
        history[1].Version.Should().Be(1);
        history[1].EventType.Should().Be("DepositMadeEvent");
        history[2].Version.Should().Be(2);
        history[2].EventType.Should().Be("DepositMadeEvent");

        // Verify timestamps are in order
        history[0].Timestamp.Should().BeOnOrBefore(history[1].Timestamp);
        history[1].Timestamp.Should().BeOnOrBefore(history[2].Timestamp);
    }

    [Fact]
    public async Task TimeTravel_TimestampBased_ReconstructsStateAtPointInTime()
    {
        // Arrange
        var accountId = "account-004";
        var streamId = $"BankAccount-{accountId}";

        // First batch of events
        var events1 = new IEvent[]
        {
            new AccountOpenedEvent { AccountId = accountId, OwnerName = "Alice", InitialBalance = 1000m }
        };
        await _eventStore.AppendAsync(streamId, events1);

        var afterOpen = DateTime.UtcNow;
        await Task.Delay(50); // Ensure timestamp difference

        // Second batch of events
        var events2 = new IEvent[]
        {
            new DepositMadeEvent { AccountId = accountId, Amount = 500m }
        };
        await _eventStore.AppendAsync(streamId, events2, 0);

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act - Get state at timestamp before deposit
        var stateBeforeDeposit = await timeTravelService.GetStateAtTimestampAsync(accountId, afterOpen);

        // Assert
        stateBeforeDeposit.Should().NotBeNull();
        stateBeforeDeposit!.Balance.Should().Be(1000m); // Only initial balance, no deposit yet
    }

    [Fact]
    public async Task TimeTravel_MultipleAggregates_IndependentHistories()
    {
        // Arrange
        var account1Id = "account-multi-1";
        var account2Id = "account-multi-2";

        await _eventStore.AppendAsync($"BankAccount-{account1Id}", new IEvent[]
        {
            new AccountOpenedEvent { AccountId = account1Id, OwnerName = "User1", InitialBalance = 100m },
            new DepositMadeEvent { AccountId = account1Id, Amount = 50m }
        });

        await _eventStore.AppendAsync($"BankAccount-{account2Id}", new IEvent[]
        {
            new AccountOpenedEvent { AccountId = account2Id, OwnerName = "User2", InitialBalance = 500m },
            new WithdrawalMadeEvent { AccountId = account2Id, Amount = 100m }
        });

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act
        var state1 = await timeTravelService.GetStateAtVersionAsync(account1Id, 1);
        var state2 = await timeTravelService.GetStateAtVersionAsync(account2Id, 1);

        // Assert
        state1!.Balance.Should().Be(150m); // 100 + 50
        state2!.Balance.Should().Be(400m); // 500 - 100
    }

    [Fact]
    public async Task TimeTravel_LargeEventStream_PerformsEfficiently()
    {
        // Arrange
        var accountId = "account-large";
        var streamId = $"BankAccount-{accountId}";

        var events = new List<IEvent>
        {
            new AccountOpenedEvent { AccountId = accountId, OwnerName = "LargeAccount", InitialBalance = 0m }
        };

        // Add 1000 deposit events
        for (int i = 0; i < 1000; i++)
        {
            events.Add(new DepositMadeEvent { AccountId = accountId, Amount = 1m });
        }

        await _eventStore.AppendAsync(streamId, events.ToArray());

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act - Time the operation
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stateAtV500 = await timeTravelService.GetStateAtVersionAsync(accountId, 500);
        sw.Stop();

        // Assert
        stateAtV500!.Balance.Should().Be(500m); // 500 deposits of 1m each
        sw.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete in under 1 second
    }

    [Fact]
    public async Task TimeTravel_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var accountId = "account-concurrent";
        var streamId = $"BankAccount-{accountId}";

        var events = new List<IEvent>
        {
            new AccountOpenedEvent { AccountId = accountId, OwnerName = "ConcurrentAccount", InitialBalance = 0m }
        };

        for (int i = 0; i < 100; i++)
        {
            events.Add(new DepositMadeEvent { AccountId = accountId, Amount = 10m });
        }

        await _eventStore.AppendAsync(streamId, events.ToArray());

        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act - Concurrent reads at different versions
        var tasks = Enumerable.Range(0, 20)
            .Select(i => timeTravelService.GetStateAtVersionAsync(accountId, i * 5).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - Each result should have correct balance
        for (int i = 0; i < 20; i++)
        {
            var expectedBalance = i * 5 * 10m; // Each version adds 10m
            results[i]!.Balance.Should().Be(expectedBalance);
        }
    }

    [Fact]
    public async Task TimeTravel_NonExistentAggregate_ReturnsNull()
    {
        // Arrange
        var timeTravelService = _serviceProvider.GetRequiredService<ITimeTravelService<BankAccount>>();

        // Act
        var state = await timeTravelService.GetStateAtVersionAsync("non-existent-account", 0);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public async Task TimeTravel_DIRegistration_WorksCorrectly()
    {
        // Arrange & Act
        var timeTravelService = _serviceProvider.GetService<ITimeTravelService<BankAccount>>();

        // Assert
        timeTravelService.Should().NotBeNull();
        timeTravelService.Should().BeOfType<TimeTravelService<BankAccount>>();
    }

    #region Test Domain

    private class BankAccount : AggregateRoot
    {
        public override string Id { get; protected set; } = string.Empty;
        public string OwnerName { get; private set; } = string.Empty;
        public decimal Balance { get; private set; }
        public bool IsClosed { get; private set; }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case AccountOpenedEvent e:
                    Id = e.AccountId;
                    OwnerName = e.OwnerName;
                    Balance = e.InitialBalance;
                    break;
                case DepositMadeEvent e:
                    Balance += e.Amount;
                    break;
                case WithdrawalMadeEvent e:
                    Balance -= e.Amount;
                    break;
                case AccountClosedEvent:
                    IsClosed = true;
                    break;
            }
        }
    }

    private record AccountOpenedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AccountId { get; init; }
        public required string OwnerName { get; init; }
        public required decimal InitialBalance { get; init; }
    }

    private record DepositMadeEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AccountId { get; init; }
        public required decimal Amount { get; init; }
    }

    private record WithdrawalMadeEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AccountId { get; init; }
        public required decimal Amount { get; init; }
    }

    private record AccountClosedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AccountId { get; init; }
    }

    #endregion
}
